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

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            IsActive = true,
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
            user.CreatedAtUtc
        );

        return new AuthResponse(
            accessToken,
            rawRefreshToken,
            DateTime.UtcNow.AddMinutes(15),
            userProfile
        );
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null || !user.IsActive || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
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
            user.CreatedAtUtc
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

        if (existingToken == null || !existingToken.IsActive || existingToken.User == null || !existingToken.User.IsActive)
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
            existingToken.User.CreatedAtUtc
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
            user.CreatedAtUtc
        );
    }
}
