using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.Common.Options;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Security;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class TwoFactorAuthServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly JwtTokenGenerator _jwtTokenGenerator;
    private readonly TwoFactorAuthService _twoFactorService;
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<IAuditLogService> _auditLogServiceMock = new();
    private readonly User _user;

    public TwoFactorAuthServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(dbOptions);

        var jwtOptions = Options.Create(new JwtOptions
        {
            SecretKey = "SUPER_SECRET_KEY_FOR_TESTING_PURPOSES_ONLY_12345",
            Issuer = "PayFlow.Api",
            Audience = "PayFlow.Client",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        _jwtTokenGenerator = new JwtTokenGenerator(jwtOptions);

        var twoFactorOptions = Options.Create(new TwoFactorOptions
        {
            Issuer = "PayFlow",
            ClockDriftToleranceSteps = 1,
            StepSizeSeconds = 30,
            Digits = 6,
            RecoveryCodesCount = 10,
            StepUpTransferThreshold = 5000.00m,
            RequireStepUpForTransfers = true
        });

        // Setup in-memory cache dictionary mock for replay tests
        var cacheStore = new Dictionary<string, object>();
        _cacheServiceMock.Setup(c => c.GetAsync<bool>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => cacheStore.TryGetValue(key, out var val) && (bool)val);

        _cacheServiceMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((string key, bool val, TimeSpan? _, CancellationToken _) =>
            {
                cacheStore[key] = val;
                return Task.CompletedTask;
            });

        _twoFactorService = new TwoFactorAuthService(
            _dbContext,
            _jwtTokenGenerator,
            twoFactorOptions,
            NullLogger<TwoFactorAuthService>.Instance,
            _cacheServiceMock.Object,
            _auditLogServiceMock.Object
        );

        _user = new User
        {
            FirstName = "Alice",
            LastName = "Smith",
            Email = "alice@payflow.local"
        };
        _user.Wallet = new Wallet { UserId = _user.Id, Balance = 50000m, Currency = "EGP" };

        _dbContext.Users.Add(_user);
        _dbContext.Wallets.Add(_user.Wallet);
        _dbContext.SaveChanges();
    }

    [Fact]
    public void GenerateTotp_ReturnsSixDigitCode()
    {
        // Arrange: known Base32 secret
        var secret = "JBSWY3DPEHPK3PXP";

        // Act
        var code = _twoFactorService.GenerateTotp(secret, DateTime.UtcNow);

        // Assert
        code.Should().HaveLength(6);
        code.All(char.IsDigit).Should().BeTrue();
    }

    [Fact]
    public void ValidateTotp_CurrentTime_ReturnsTrue()
    {
        // Arrange
        var secret = "JBSWY3DPEHPK3PXP";
        var now = DateTime.UtcNow;
        var code = _twoFactorService.GenerateTotp(secret, now);

        // Act
        var isValid = _twoFactorService.ValidateTotp(secret, code, toleranceSteps: 1);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateTotp_ClockSkewWithinTolerance_ReturnsTrue()
    {
        // Arrange
        var secret = "JBSWY3DPEHPK3PXP";
        var pastCode = _twoFactorService.GenerateTotp(secret, DateTime.UtcNow.AddSeconds(-25));
        var futureCode = _twoFactorService.GenerateTotp(secret, DateTime.UtcNow.AddSeconds(25));

        // Act & Assert
        _twoFactorService.ValidateTotp(secret, pastCode, toleranceSteps: 1).Should().BeTrue();
        _twoFactorService.ValidateTotp(secret, futureCode, toleranceSteps: 1).Should().BeTrue();
    }

    [Fact]
    public void ValidateTotp_ExpiredOrOutsideTolerance_ReturnsFalse()
    {
        // Arrange: +/- 65 seconds (exceeds +/- 30s tolerance)
        var secret = "JBSWY3DPEHPK3PXP";
        var expiredCode = _twoFactorService.GenerateTotp(secret, DateTime.UtcNow.AddSeconds(-65));
        var futureCode = _twoFactorService.GenerateTotp(secret, DateTime.UtcNow.AddSeconds(65));

        // Act & Assert
        _twoFactorService.ValidateTotp(secret, expiredCode, toleranceSteps: 1).Should().BeFalse();
        _twoFactorService.ValidateTotp(secret, futureCode, toleranceSteps: 1).Should().BeFalse();
    }

    [Fact]
    public void ValidateTotp_ReplayAttack_RejectsDuplicateCode()
    {
        // Arrange
        var secret = "JBSWY3DPEHPK3PXP";
        var code = _twoFactorService.GenerateTotp(secret, DateTime.UtcNow);
        var replayKey = $"replay:test:{_user.Id}";

        // Act 1: Initial submission succeeds
        var firstAttempt = _twoFactorService.ValidateTotp(secret, code, toleranceSteps: 1, replayKeyPrefix: replayKey);

        // Act 2: Replay same code in same window
        var secondAttempt = _twoFactorService.ValidateTotp(secret, code, toleranceSteps: 1, replayKeyPrefix: replayKey);

        // Assert
        firstAttempt.Should().BeTrue();
        secondAttempt.Should().BeFalse();
    }

    [Fact]
    public async Task SetupAsync_GeneratesSharedKeyQrCodeAndTenRecoveryCodes()
    {
        // Act
        var setup = await _twoFactorService.SetupAsync(_user.Id);

        // Assert
        setup.SharedKey.Should().NotBeNullOrWhiteSpace();
        setup.AuthenticatorUri.Should().StartWith("otpauth://totp/");
        setup.AuthenticatorUri.Should().Contain("alice%40payflow.local");
        setup.QrCodeDataUri.Should().StartWith("data:image/png;base64,");
        setup.RecoveryCodes.Should().HaveCount(10);
        setup.RecoveryCodes.All(c => c.Length == 9 && c[4] == '-').Should().BeTrue();

        // Verify stored in DB
        var userInDb = await _dbContext.Users.FindAsync(_user.Id);
        userInDb!.TwoFactorSecret.Should().Be(setup.SharedKey);
        userInDb.TwoFactorEnabled.Should().BeFalse(); // Not enabled until confirmed!

        var recoveryCount = await _dbContext.TwoFactorRecoveryCodes.CountAsync(r => r.UserId == _user.Id && !r.IsUsed);
        recoveryCount.Should().Be(10);
    }

    [Fact]
    public async Task EnableAsync_ValidCode_EnablesTwoFactor()
    {
        // Arrange: Setup first
        var setup = await _twoFactorService.SetupAsync(_user.Id);
        var validCode = _twoFactorService.GenerateTotp(setup.SharedKey);

        // Act
        var result = await _twoFactorService.EnableAsync(_user.Id, validCode);

        // Assert
        result.Should().BeTrue();

        var userInDb = await _dbContext.Users.FindAsync(_user.Id);
        userInDb!.TwoFactorEnabled.Should().BeTrue();
        userInDb.TwoFactorEnabledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task EnableAsync_InvalidCode_FailsAndLeavesDisabled()
    {
        // Arrange
        await _twoFactorService.SetupAsync(_user.Id);

        // Act
        var result = await _twoFactorService.EnableAsync(_user.Id, "000000");

        // Assert
        result.Should().BeFalse();

        var userInDb = await _dbContext.Users.FindAsync(_user.Id);
        userInDb!.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task DisableAsync_ValidCode_DisablesTwoFactorAndCleansUp()
    {
        // Arrange: Enable 2FA first
        var setup = await _twoFactorService.SetupAsync(_user.Id);
        var code = _twoFactorService.GenerateTotp(setup.SharedKey);
        await _twoFactorService.EnableAsync(_user.Id, code);

        // Act: Disable 2FA
        var disableCode = _twoFactorService.GenerateTotp(setup.SharedKey);
        var disabled = await _twoFactorService.DisableAsync(_user.Id, disableCode);

        // Assert
        disabled.Should().BeTrue();

        var userInDb = await _dbContext.Users.FindAsync(_user.Id);
        userInDb!.TwoFactorEnabled.Should().BeFalse();
        userInDb.TwoFactorSecret.Should().BeNull();

        var remainingCodes = await _dbContext.TwoFactorRecoveryCodes.CountAsync(r => r.UserId == _user.Id);
        remainingCodes.Should().Be(0);
    }

    [Fact]
    public async Task VerifyTwoFactorLoginAsync_ValidTotp_IssuesTokens()
    {
        // Arrange: Enable 2FA
        var setup = await _twoFactorService.SetupAsync(_user.Id);
        var code = _twoFactorService.GenerateTotp(setup.SharedKey);
        await _twoFactorService.EnableAsync(_user.Id, code);

        var challengeTicket = _jwtTokenGenerator.GenerateTwoFactorChallengeToken(_user);
        var loginCode = _twoFactorService.GenerateTotp(setup.SharedKey);

        // Act
        var authResponse = await _twoFactorService.VerifyTwoFactorLoginAsync(new VerifyTwoFactorRequest(challengeTicket, loginCode));

        // Assert
        authResponse.Should().NotBeNull();
        authResponse.RequiresTwoFactor.Should().BeFalse();
        authResponse.AccessToken.Should().NotBeNullOrWhiteSpace();
        authResponse.RefreshToken.Should().NotBeNullOrWhiteSpace();
        authResponse.User!.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyTwoFactorLoginAsync_ValidRecoveryCode_ConsumesCodeAndIssuesTokens()
    {
        // Arrange: Enable 2FA
        var setup = await _twoFactorService.SetupAsync(_user.Id);
        var enableCode = _twoFactorService.GenerateTotp(setup.SharedKey);
        await _twoFactorService.EnableAsync(_user.Id, enableCode);

        var recoveryCode = setup.RecoveryCodes[0];
        var challengeTicket = _jwtTokenGenerator.GenerateTwoFactorChallengeToken(_user);

        // Act 1: Verify using single-use backup code
        var response = await _twoFactorService.VerifyTwoFactorLoginAsync(new VerifyTwoFactorRequest(challengeTicket, recoveryCode));

        // Assert 1: Successful login
        response.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Verify recovery code was marked as used in DB
        var usedCode = await _dbContext.TwoFactorRecoveryCodes.FirstAsync(r => r.UserId == _user.Id && r.IsUsed);
        usedCode.UsedAtUtc.Should().NotBeNull();

        // Act 2: Attempt to reuse the SAME recovery code with a new challenge
        var ticket2 = _jwtTokenGenerator.GenerateTwoFactorChallengeToken(_user);
        var act2 = () => _twoFactorService.VerifyTwoFactorLoginAsync(new VerifyTwoFactorRequest(ticket2, recoveryCode));

        // Assert 2: Rejected as single-use
        await act2.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*Invalid or expired*");
    }

    [Fact]
    public async Task ValidateStepUpTransferAsync_AboveThresholdWithoutCode_ThrowsTwoFactorRequiredException()
    {
        // Arrange
        var setup = await _twoFactorService.SetupAsync(_user.Id);
        var enableCode = _twoFactorService.GenerateTotp(setup.SharedKey);
        await _twoFactorService.EnableAsync(_user.Id, enableCode);

        // Act: 10,000 > threshold of 5,000 without code
        var act = () => _twoFactorService.ValidateStepUpTransferAsync(_user, 10000m, twoFactorCode: null);

        // Assert
        var ex = await act.Should().ThrowAsync<TwoFactorRequiredException>();
        ex.Which.ThresholdAmount.Should().Be(5000.00m);
        ex.Which.AttemptedAmount.Should().Be(10000m);
    }

    [Fact]
    public async Task ValidateStepUpTransferAsync_AboveThresholdWithValidCode_Succeeds()
    {
        // Arrange
        var setup = await _twoFactorService.SetupAsync(_user.Id);
        var enableCode = _twoFactorService.GenerateTotp(setup.SharedKey);
        await _twoFactorService.EnableAsync(_user.Id, enableCode);

        var validCode = _twoFactorService.GenerateTotp(setup.SharedKey);

        // Act & Assert
        var act = () => _twoFactorService.ValidateStepUpTransferAsync(_user, 10000m, twoFactorCode: validCode);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateStepUpTransferAsync_BelowThresholdWithoutCode_Succeeds()
    {
        // Arrange
        var setup = await _twoFactorService.SetupAsync(_user.Id);
        var enableCode = _twoFactorService.GenerateTotp(setup.SharedKey);
        await _twoFactorService.EnableAsync(_user.Id, enableCode);

        // Act & Assert: 1,000 < threshold of 5,000 -> succeeds without 2FA code
        var act = () => _twoFactorService.ValidateStepUpTransferAsync(_user, 1000m, twoFactorCode: null);
        await act.Should().NotThrowAsync();
    }
}
