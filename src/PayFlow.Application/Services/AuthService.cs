using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class AuthService : IAuthService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IAuditLogService? _auditLogService;

    public AuthService(
        IPayFlowDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IAuditLogService? auditLogService = null)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _auditLogService = auditLogService;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existingUser)
        {
            throw new InvalidTransactionException($"Email '{request.Email}' is already registered.");
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var rawVerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var verificationTokenHash = HashSha256(rawVerificationToken);
        var verificationTokenExpiresAt = DateTime.UtcNow.AddHours(24);

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            IsActive = false,
            EmailConfirmed = false,
            EmailVerificationTokenHash = verificationTokenHash,
            EmailVerificationTokenExpiresAtUtc = verificationTokenExpiresAt,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var selectedCurrency = CurrencyConstants.IsSupported(request.Currency)
            ? request.Currency.Trim().ToUpperInvariant()
            : CurrencyConstants.Egp;

        var wallet = new Wallet
        {
            UserId = user.Id,
            Balance = 0.00m,
            Currency = selectedCurrency,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        user.Wallet = wallet;

        _dbContext.Users.Add(user);
        _dbContext.Wallets.Add(wallet);

        // Publish UserRegisteredEvent to Outbox for asynchronous email delivery
        var userRegisteredPayload = JsonSerializer.Serialize(new
        {
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            VerificationToken = rawVerificationToken,
            ExpiresAtUtc = verificationTokenExpiresAt,
            OccurredAtUtc = DateTime.UtcNow
        });

        var outboxMessage = new OutboxMessage
        {
            Type = "UserRegisteredEvent",
            Payload = userRegisteredPayload,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                user.Id,
                "UserRegister",
                user.Id.ToString(),
                true,
                cancellationToken: cancellationToken);
        }

        var userProfile = new UserProfileResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.IsActive,
            user.CreatedAtUtc,
            user.KycTier.ToString(),
            user.TwoFactorEnabled,
            user.Role.ToString(),
            user.EmailConfirmed
        );

        return new AuthResponse(
            string.Empty,
            string.Empty,
            DateTime.UtcNow,
            userProfile,
            Message: "Registration successful. Please check your email to verify your account before logging in."
        );
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null || !user.IsActive || !user.EmailConfirmed || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            if (_auditLogService != null)
            {
                await _auditLogService.LogAsync(
                    user?.Id,
                    "UserLogin",
                    normalizedEmail,
                    false,
                    "Invalid email or password.",
                    cancellationToken);
            }

            throw new UnauthorizedDomainException("Invalid email or password.");
        }

        if (user.TwoFactorEnabled)
        {
            var twoFactorChallengeTicket = _jwtTokenGenerator.GenerateTwoFactorChallengeToken(user);
            return new AuthResponse(
                AccessToken: string.Empty,
                RefreshToken: string.Empty,
                AccessTokenExpiresAtUtc: DateTime.UtcNow.AddMinutes(5),
                User: null,
                RequiresTwoFactor: true,
                TwoFactorToken: twoFactorChallengeTicket
            );
        }

        var (accessToken, rawRefreshToken, refreshTokenExpiresAt) = _jwtTokenGenerator.GenerateTokens(user);
        var tokenHash = _jwtTokenGenerator.HashToken(rawRefreshToken);

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = refreshTokenExpiresAt,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                user.Id,
                "UserLogin",
                user.Id.ToString(),
                true,
                cancellationToken: cancellationToken);
        }

        var userProfile = new UserProfileResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.IsActive,
            user.CreatedAtUtc,
            user.KycTier.ToString(),
            user.TwoFactorEnabled,
            user.Role.ToString(),
            user.EmailConfirmed
        );

        return new AuthResponse(
            accessToken,
            rawRefreshToken,
            DateTime.UtcNow.AddMinutes(15),
            userProfile
        );
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new UnauthorizedDomainException("Refresh token is required.");
        }

        var tokenHash = _jwtTokenGenerator.HashToken(request.RefreshToken);

        var existingToken = await _dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

        if (existingToken == null || !existingToken.IsActive || existingToken.User == null || !existingToken.User.IsActive || !existingToken.User.EmailConfirmed)
        {
            if (_auditLogService != null)
            {
                await _auditLogService.LogAsync(
                    existingToken?.UserId,
                    "TokenRefresh",
                    null,
                    false,
                    "Invalid or expired refresh token.",
                    cancellationToken);
            }

            throw new UnauthorizedDomainException("Invalid or expired refresh token.");
        }

        // Revoke current token (Refresh Token Rotation)
        existingToken.RevokedAtUtc = DateTime.UtcNow;

        var (newAccessToken, newRawRefreshToken, newRefreshTokenExpiresAt) = _jwtTokenGenerator.GenerateTokens(existingToken.User);
        var newTokenHash = _jwtTokenGenerator.HashToken(newRawRefreshToken);

        existingToken.ReplacedByTokenHash = newTokenHash;

        var newRefreshToken = new RefreshToken
        {
            UserId = existingToken.UserId,
            TokenHash = newTokenHash,
            ExpiresAtUtc = newRefreshTokenExpiresAt,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                existingToken.User.Id,
                "TokenRefresh",
                existingToken.User.Id.ToString(),
                true,
                cancellationToken: cancellationToken);
        }

        var userProfile = new UserProfileResponse(
            existingToken.User.Id,
            existingToken.User.FirstName,
            existingToken.User.LastName,
            existingToken.User.Email,
            existingToken.User.IsActive,
            existingToken.User.CreatedAtUtc,
            existingToken.User.KycTier.ToString(),
            existingToken.User.TwoFactorEnabled,
            existingToken.User.Role.ToString(),
            existingToken.User.EmailConfirmed
        );

        return new AuthResponse(
            newAccessToken,
            newRawRefreshToken,
            DateTime.UtcNow.AddMinutes(15),
            userProfile
        );
    }

    public async Task RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return;
        }

        var tokenHash = _jwtTokenGenerator.HashToken(request.RefreshToken);

        var existingToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

        if (existingToken != null && existingToken.IsActive)
        {
            existingToken.RevokedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (_auditLogService != null)
            {
                await _auditLogService.LogAsync(
                    existingToken.UserId,
                    "TokenRevoke",
                    existingToken.UserId.ToString(),
                    true,
                    cancellationToken: cancellationToken);
            }
        }
    }

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        return new UserProfileResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.IsActive,
            user.CreatedAtUtc,
            user.KycTier.ToString(),
            user.TwoFactorEnabled,
            user.Role.ToString(),
            user.EmailConfirmed
        );
    }

    public async Task<MessageResponse> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidTransactionException("Verification token is required.");
        }

        var tokenHash = HashSha256(token.Trim());

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.EmailVerificationTokenHash == tokenHash, cancellationToken);

        if (user == null || user.EmailVerificationTokenExpiresAtUtc == null || user.EmailVerificationTokenExpiresAtUtc < DateTime.UtcNow)
        {
            throw new InvalidTransactionException("The email verification token is invalid or has expired.");
        }

        user.EmailConfirmed = true;
        user.IsActive = true;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAtUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                user.Id,
                "EmailVerified",
                user.Email,
                true,
                cancellationToken: cancellationToken);
        }

        return new MessageResponse("Email successfully verified. Your account is now active.");
    }

    public async Task<MessageResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // Anti-Enumeration: Return identical success response regardless of whether the email exists
        if (user == null || !user.IsActive)
        {
            return new MessageResponse("If your email is registered in our system, you will receive password reset instructions shortly.");
        }

        var rawResetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var resetTokenHash = HashSha256(rawResetToken);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(15);

        user.PasswordResetTokenHash = resetTokenHash;
        user.PasswordResetTokenExpiresAtUtc = expiresAtUtc;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var payload = JsonSerializer.Serialize(new
        {
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ResetToken = rawResetToken,
            ExpiresAtUtc = expiresAtUtc,
            OccurredAtUtc = DateTime.UtcNow
        });

        var outboxMessage = new OutboxMessage
        {
            Type = "PasswordResetRequestedEvent",
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                user.Id,
                "PasswordResetRequested",
                normalizedEmail,
                true,
                cancellationToken: cancellationToken);
        }

        return new MessageResponse("If your email is registered in our system, you will receive password reset instructions shortly.");
    }

    public async Task<MessageResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new InvalidTransactionException("Reset token is required.");
        }

        var tokenHash = HashSha256(request.Token.Trim());

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, cancellationToken);

        if (user == null || user.PasswordResetTokenExpiresAtUtc == null || user.PasswordResetTokenExpiresAtUtc < DateTime.UtcNow)
        {
            throw new InvalidTransactionException("The password reset token is invalid or has expired.");
        }

        // Update password and clear reset token
        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAtUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;

        // Revoke all active refresh tokens for this user immediately
        var activeRefreshTokens = await _dbContext.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var rt in activeRefreshTokens)
        {
            rt.RevokedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                user.Id,
                "PasswordResetCompleted",
                user.Id.ToString(),
                true,
                cancellationToken: cancellationToken);
        }

        return new MessageResponse("Password has been successfully reset. You may now log in with your new password.");
    }

    private static string HashSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
