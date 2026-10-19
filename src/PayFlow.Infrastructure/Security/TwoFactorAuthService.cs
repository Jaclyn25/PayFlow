using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.Common.Options;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using QRCoder;

namespace PayFlow.Infrastructure.Security;

public class TwoFactorAuthService : ITwoFactorAuthService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const string RecoveryCodeChars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private readonly IPayFlowDbContext _dbContext;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ICacheService? _cacheService;
    private readonly IAuditLogService? _auditLogService;
    private readonly TwoFactorOptions _options;
    private readonly ILogger<TwoFactorAuthService> _logger;

    public TwoFactorAuthService(
        IPayFlowDbContext dbContext,
        IJwtTokenGenerator jwtTokenGenerator,
        IOptions<TwoFactorOptions> options,
        ILogger<TwoFactorAuthService> logger,
        ICacheService? cacheService = null,
        IAuditLogService? auditLogService = null)
    {
        _dbContext = dbContext;
        _jwtTokenGenerator = jwtTokenGenerator;
        _cacheService = cacheService;
        _auditLogService = auditLogService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<TwoFactorSetupResponse> SetupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.TwoFactorRecoveryCodes)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        // 1. Generate 160-bit (20 bytes) Base32 secret key
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var sharedKey = ToBase32String(secretBytes);

        // Store secret temporarily (TwoFactorEnabled remains false until verified)
        user.TwoFactorSecret = sharedKey;

        // 2. Generate 10 single-use recovery codes
        var plainRecoveryCodes = new List<string>(_options.RecoveryCodesCount);
        var oldUnusedCodes = user.TwoFactorRecoveryCodes.Where(r => !r.IsUsed).ToList();
        foreach (var oldCode in oldUnusedCodes)
        {
            _dbContext.TwoFactorRecoveryCodes.Remove(oldCode);
        }

        for (int i = 0; i < _options.RecoveryCodesCount; i++)
        {
            var plainCode = GenerateRecoveryCode();
            plainRecoveryCodes.Add(plainCode);

            var codeHash = ComputeSha256Hash(plainCode);
            var recoveryEntity = new TwoFactorRecoveryCode
            {
                UserId = user.Id,
                CodeHash = codeHash,
                IsUsed = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.TwoFactorRecoveryCodes.Add(recoveryEntity);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 3. Format RFC 6238 otpauth URI
        var authenticatorUri = $"otpauth://totp/{Uri.EscapeDataString(_options.Issuer)}:{Uri.EscapeDataString(user.Email)}?secret={sharedKey}&issuer={Uri.EscapeDataString(_options.Issuer)}&algorithm=SHA1&digits={_options.Digits}&period={_options.StepSizeSeconds}";

        // 4. Generate QR code PNG data URI
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(authenticatorUri, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = qrCode.GetGraphic(20);
        var qrCodeDataUri = $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";

        return new TwoFactorSetupResponse(
            SharedKey: sharedKey,
            AuthenticatorUri: authenticatorUri,
            QrCodeDataUri: qrCodeDataUri,
            RecoveryCodes: plainRecoveryCodes
        );
    }

    public async Task<bool> EnableAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        if (string.IsNullOrWhiteSpace(user.TwoFactorSecret))
        {
            throw new InvalidTransactionException("Two-factor authentication setup has not been initiated.");
        }

        var isValid = ValidateTotp(user.TwoFactorSecret, code, _options.ClockDriftToleranceSteps, $"2fa:used:enable:{userId}");
        if (!isValid)
        {
            return false;
        }

        user.TwoFactorEnabled = true;
        user.TwoFactorEnabledAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                userId,
                "TwoFactorEnabled",
                user.Id.ToString(),
                true,
                cancellationToken: cancellationToken);
        }

        return true;
    }

    public async Task<bool> DisableAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.TwoFactorRecoveryCodes)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        if (!user.TwoFactorEnabled)
        {
            return true;
        }

        var isValid = ValidateTotp(user.TwoFactorSecret ?? string.Empty, code, _options.ClockDriftToleranceSteps, $"2fa:used:disable:{userId}")
                   || await ValidateAndConsumeRecoveryCodeAsync(user.Id, code, cancellationToken);

        if (!isValid)
        {
            return false;
        }

        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorEnabledAtUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;

        // Clean up recovery codes
        var codesToRemove = user.TwoFactorRecoveryCodes.ToList();
        foreach (var c in codesToRemove)
        {
            _dbContext.TwoFactorRecoveryCodes.Remove(c);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                userId,
                "TwoFactorDisabled",
                user.Id.ToString(),
                true,
                cancellationToken: cancellationToken);
        }

        return true;
    }

    public async Task<TwoFactorStatusResponse> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        return new TwoFactorStatusResponse(user.TwoFactorEnabled, user.TwoFactorEnabledAtUtc);
    }

    public async Task<AuthResponse> VerifyTwoFactorLoginAsync(VerifyTwoFactorRequest request, CancellationToken cancellationToken = default)
    {
        var (isValidToken, userId) = _jwtTokenGenerator.ValidateTwoFactorChallengeToken(request.TwoFactorToken);
        if (!isValidToken || userId == Guid.Empty)
        {
            throw new InvalidTransactionException("Invalid or expired two-factor verification ticket.");
        }

        var user = await _dbContext.Users
            .Include(u => u.TwoFactorRecoveryCodes)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null || !user.IsActive)
        {
            throw new InvalidTransactionException("User not found or account is deactivated.");
        }

        if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.TwoFactorSecret))
        {
            throw new InvalidTransactionException("Two-factor authentication is not configured for this account.");
        }

        // Check TOTP code first
        var isValidCode = ValidateTotp(user.TwoFactorSecret, request.Code, _options.ClockDriftToleranceSteps, $"2fa:used:login:{userId}");

        // If not valid TOTP, try recovery code
        if (!isValidCode)
        {
            isValidCode = await ValidateAndConsumeRecoveryCodeAsync(user.Id, request.Code, cancellationToken);
        }

        if (!isValidCode)
        {
            throw new InvalidTransactionException("Invalid or expired two-factor authentication code.");
        }

        // Issue full JWT access & refresh tokens
        var (accessToken, rawRefreshToken, refreshTokenExpiresAtUtc) = _jwtTokenGenerator.GenerateTokens(user);

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _jwtTokenGenerator.HashToken(rawRefreshToken),
            ExpiresAtUtc = refreshTokenExpiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var userProfile = new UserProfileResponse(
            Id: user.Id,
            FirstName: user.FirstName,
            LastName: user.LastName,
            Email: user.Email,
            IsActive: user.IsActive,
            CreatedAtUtc: user.CreatedAtUtc,
            KycTier: user.KycTier.ToString(),
            TwoFactorEnabled: user.TwoFactorEnabled
        );

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: rawRefreshToken,
            AccessTokenExpiresAtUtc: DateTime.UtcNow.AddMinutes(15),
            User: userProfile,
            RequiresTwoFactor: false
        );
    }

    public async Task ValidateStepUpTransferAsync(
        User user,
        decimal amount,
        string? twoFactorCode,
        CancellationToken cancellationToken = default)
    {
        if (!user.TwoFactorEnabled)
        {
            return;
        }

        if (!_options.RequireStepUpForTransfers || amount < _options.StepUpTransferThreshold)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(twoFactorCode))
        {
            throw new TwoFactorRequiredException(_options.StepUpTransferThreshold, amount);
        }

        var isValid = ValidateTotp(user.TwoFactorSecret ?? string.Empty, twoFactorCode, _options.ClockDriftToleranceSteps, $"2fa:used:transfer:{user.Id}")
                   || await ValidateAndConsumeRecoveryCodeAsync(user.Id, twoFactorCode, cancellationToken);

        if (!isValid)
        {
            throw new InvalidTransactionException("Invalid two-factor authentication code for high-value transfer authorization.");
        }
    }

    public string GenerateTotp(string secret, DateTime? timestampUtc = null)
    {
        var secretBytes = FromBase32String(secret);
        var time = timestampUtc ?? DateTime.UtcNow;
        var unixTime = new DateTimeOffset(time).ToUnixTimeSeconds();
        var timeStep = unixTime / _options.StepSizeSeconds;

        return ComputeTotpForStep(secretBytes, timeStep, _options.Digits);
    }

    public bool ValidateTotp(string secret, string code, int toleranceSteps = 1, string? replayKeyPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var cleanCode = code.Trim().Replace(" ", string.Empty);
        if (cleanCode.Length != _options.Digits || !cleanCode.All(char.IsDigit))
        {
            return false;
        }

        byte[] secretBytes;
        try
        {
            secretBytes = FromBase32String(secret);
        }
        catch
        {
            return false;
        }

        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentStep = unixTime / _options.StepSizeSeconds;

        for (int stepOffset = -toleranceSteps; stepOffset <= toleranceSteps; stepOffset++)
        {
            var testStep = currentStep + stepOffset;
            var expectedCode = ComputeTotpForStep(secretBytes, testStep, _options.Digits);

            if (CryptographicEquals(expectedCode, cleanCode))
            {
                // Replay attack prevention: check and record step in cache
                if (!string.IsNullOrWhiteSpace(replayKeyPrefix) && _cacheService != null)
                {
                    var cacheKey = $"{replayKeyPrefix}:{testStep}";
                    var isUsed = _cacheService.GetAsync<bool>(cacheKey).GetAwaiter().GetResult();
                    if (isUsed)
                    {
                        _logger.LogWarning("Rejected duplicate TOTP code replay for step {Step}", testStep);
                        return false;
                    }

                    _cacheService.SetAsync(cacheKey, true, TimeSpan.FromSeconds(90)).GetAwaiter().GetResult();
                }

                return true;
            }
        }

        return false;
    }

    private async Task<bool> ValidateAndConsumeRecoveryCodeAsync(Guid userId, string rawCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        var normalizedCode = rawCode.Trim().ToUpperInvariant();
        var codeHash = ComputeSha256Hash(normalizedCode);

        var recoveryCode = await _dbContext.TwoFactorRecoveryCodes
            .FirstOrDefaultAsync(r => r.UserId == userId && r.CodeHash == codeHash && !r.IsUsed, cancellationToken);

        if (recoveryCode == null)
        {
            return false;
        }

        // Revoke immediately upon consumption
        recoveryCode.IsUsed = true;
        recoveryCode.UsedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Recovery code consumed successfully for user {UserId}", userId);
        return true;
    }

    private static string ComputeTotpForStep(byte[] secretBytes, long timeStep, int digits)
    {
        var timeBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(timeBytes);
        }

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(timeBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, digits);
        var otp = binaryCode % modulo;

        return otp.ToString(new string('0', digits));
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static string GenerateRecoveryCode()
    {
        var part1 = RandomNumberGenerator.GetString(RecoveryCodeChars, 4);
        var part2 = RandomNumberGenerator.GetString(RecoveryCodeChars, 4);
        return $"{part1}-{part2}";
    }

    private static string ComputeSha256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static string ToBase32String(byte[] data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int currentByte = 0;
        int bitsRemaining = 8;
        int mask = 0;

        foreach (var b in data)
        {
            currentByte = (currentByte << 8) | b;
            bitsRemaining += 8;
            while (bitsRemaining >= 5)
            {
                mask = (currentByte >> (bitsRemaining - 5)) & 0x1F;
                result.Append(Base32Alphabet[mask]);
                bitsRemaining -= 5;
            }
        }

        if (bitsRemaining > 0)
        {
            mask = (currentByte << (5 - bitsRemaining)) & 0x1F;
            result.Append(Base32Alphabet[mask]);
        }

        return result.ToString();
    }

    private static byte[] FromBase32String(string input)
    {
        var cleanInput = input.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>((cleanInput.Length * 5) / 8);

        int buffer = 0;
        int bitsLeft = 0;

        foreach (var c in cleanInput)
        {
            var charValue = Base32Alphabet.IndexOf(c);
            if (charValue < 0)
            {
                throw new ArgumentException($"Invalid Base32 character '{c}'.");
            }

            buffer = (buffer << 5) | charValue;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }
}
