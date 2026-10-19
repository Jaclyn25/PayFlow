using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class AuthServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGeneratorMock = new();
    private readonly Mock<IAuditLogService> _auditLogServiceMock = new();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>()))
            .Returns<string>(pwd => $"hashed_{pwd}");

        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((pwd, hash) => hash == $"hashed_{pwd}");

        _jwtTokenGeneratorMock.Setup(j => j.GenerateTokens(It.IsAny<User>()))
            .Returns(("access_token_123", "raw_refresh_token_123", DateTime.UtcNow.AddDays(7)));

        _jwtTokenGeneratorMock.Setup(j => j.HashToken(It.IsAny<string>()))
            .Returns<string>(token => $"hashed_{token}");

        _authService = new AuthService(
            _dbContext,
            _passwordHasherMock.Object,
            _jwtTokenGeneratorMock.Object,
            _auditLogServiceMock.Object);
    }

    [Fact]
    public async Task RegisterAsync_NewUser_CreatesUserWalletAndOutboxEvent()
    {
        // Arrange
        var request = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");

        // Act
        var response = await _authService.RegisterAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.User!.Email.Should().Be("alice@example.com");
        response.User.IsActive.Should().BeFalse();
        response.User.EmailConfirmed.Should().BeFalse();

        var userInDb = await _dbContext.Users.Include(u => u.Wallet).FirstOrDefaultAsync(u => u.Email == "alice@example.com");
        userInDb.Should().NotBeNull();
        userInDb!.Wallet.Should().NotBeNull();
        userInDb.Wallet!.Balance.Should().Be(0.00m);
        userInDb.IsActive.Should().BeFalse();
        userInDb.EmailConfirmed.Should().BeFalse();
        userInDb.EmailVerificationTokenHash.Should().NotBeNullOrWhiteSpace();
        userInDb.EmailVerificationTokenExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);

        var outboxMessage = await _dbContext.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "UserRegisteredEvent");
        outboxMessage.Should().NotBeNull();
        outboxMessage!.Payload.Should().Contain("alice@example.com");
        outboxMessage.Payload.Should().Contain("VerificationToken");
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsInvalidTransactionException()
    {
        // Arrange
        var request = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(request);

        // Act
        var act = () => _authService.RegisterAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public async Task VerifyEmailAsync_ValidToken_ActivatesUserAndConfirmsEmail()
    {
        // Arrange
        var request = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(request);

        var outboxMessage = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "UserRegisteredEvent");
        var doc = JsonDocument.Parse(outboxMessage.Payload);
        var verificationToken = doc.RootElement.GetProperty("VerificationToken").GetString();

        // Act
        var result = await _authService.VerifyEmailAsync(verificationToken!);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Contain("verified");

        var userInDb = await _dbContext.Users.FirstAsync(u => u.Email == "alice@example.com");
        userInDb.EmailConfirmed.Should().BeTrue();
        userInDb.IsActive.Should().BeTrue();
        userInDb.EmailVerificationTokenHash.Should().BeNull();
        userInDb.EmailVerificationTokenExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task VerifyEmailAsync_InvalidOrExpiredToken_ThrowsInvalidTransactionException()
    {
        // Act: Invalid token
        var actInvalid = () => _authService.VerifyEmailAsync("invalid-token-123");
        await actInvalid.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*invalid or has expired*");

        // Arrange expired token
        var request = new RegisterRequest("Bob", "Builder", "bob@example.com", "Password123!");
        await _authService.RegisterAsync(request);

        var user = await _dbContext.Users.FirstAsync(u => u.Email == "bob@example.com");
        user.EmailVerificationTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10);
        await _dbContext.SaveChangesAsync();

        var outboxMessage = await _dbContext.OutboxMessages
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstAsync(m => m.Type == "UserRegisteredEvent");
        var doc = JsonDocument.Parse(outboxMessage.Payload);
        var token = doc.RootElement.GetProperty("VerificationToken").GetString();

        // Act: Expired token
        var actExpired = () => _authService.VerifyEmailAsync(token!);
        await actExpired.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*invalid or has expired*");
    }

    [Fact]
    public async Task LoginAsync_UnverifiedUser_ThrowsUnauthorizedDomainException()
    {
        // Arrange: Unverified user
        var registerRequest = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(registerRequest);

        var loginRequest = new LoginRequest("alice@example.com", "Password123!");

        // Act
        var act = () => _authService.LoginAsync(loginRequest);

        // Assert: Generic message protects against enumeration
        await act.Should().ThrowAsync<UnauthorizedDomainException>()
            .WithMessage("*Invalid email or password*");
    }

    [Fact]
    public async Task LoginAsync_VerifiedUser_ReturnsAuthResponse()
    {
        // Arrange
        var registerRequest = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(registerRequest);

        var outboxMessage = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "UserRegisteredEvent");
        var doc = JsonDocument.Parse(outboxMessage.Payload);
        var verificationToken = doc.RootElement.GetProperty("VerificationToken").GetString();
        await _authService.VerifyEmailAsync(verificationToken!);

        var loginRequest = new LoginRequest("alice@example.com", "Password123!");

        // Act
        var response = await _authService.LoginAsync(loginRequest);

        // Assert
        response.Should().NotBeNull();
        response.AccessToken.Should().Be("access_token_123");
        response.RefreshToken.Should().Be("raw_refresh_token_123");
        response.User!.EmailConfirmed.Should().BeTrue();
        response.User.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ThrowsUnauthorizedDomainException()
    {
        // Arrange
        var registerRequest = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(registerRequest);

        var outboxMessage = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "UserRegisteredEvent");
        var doc = JsonDocument.Parse(outboxMessage.Payload);
        var verificationToken = doc.RootElement.GetProperty("VerificationToken").GetString();
        await _authService.VerifyEmailAsync(verificationToken!);

        var loginRequest = new LoginRequest("alice@example.com", "WrongPassword!");

        // Act
        var act = () => _authService.LoginAsync(loginRequest);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedDomainException>()
            .WithMessage("*Invalid email or password*");
    }

    [Fact]
    public async Task ForgotPasswordAsync_ExistingActiveUser_Generates15MinTokenAndEnqueuesOutboxEvent()
    {
        // Arrange
        var registerRequest = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(registerRequest);

        var outboxMessage = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "UserRegisteredEvent");
        var doc = JsonDocument.Parse(outboxMessage.Payload);
        var verificationToken = doc.RootElement.GetProperty("VerificationToken").GetString();
        await _authService.VerifyEmailAsync(verificationToken!);

        // Act
        var response = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest("alice@example.com"));

        // Assert
        response.Should().NotBeNull();
        response.Message.Should().Contain("instructions shortly");

        var userInDb = await _dbContext.Users.FirstAsync(u => u.Email == "alice@example.com");
        userInDb.PasswordResetTokenHash.Should().NotBeNullOrWhiteSpace();
        userInDb.PasswordResetTokenExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        userInDb.PasswordResetTokenExpiresAtUtc.Should().BeBefore(DateTime.UtcNow.AddMinutes(16));

        var resetOutbox = await _dbContext.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "PasswordResetRequestedEvent");
        resetOutbox.Should().NotBeNull();
        resetOutbox!.Payload.Should().Contain("alice@example.com");
        resetOutbox.Payload.Should().Contain("ResetToken");
    }

    [Fact]
    public async Task ForgotPasswordAsync_NonexistentOrInactiveUser_ReturnsIdenticalResponseWithoutOutboxEvent()
    {
        // Act: Nonexistent user
        var responseNonexistent = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest("nonexistent@example.com"));

        // Assert: Identical response for anti-enumeration
        responseNonexistent.Should().NotBeNull();
        responseNonexistent.Message.Should().Contain("instructions shortly");

        var outboxCount = await _dbContext.OutboxMessages.CountAsync(m => m.Type == "PasswordResetRequestedEvent");
        outboxCount.Should().Be(0);

        // Arrange: Inactive/unverified user
        await _authService.RegisterAsync(new RegisterRequest("Bob", "Builder", "bob@example.com", "Password123!"));

        // Act: Inactive user
        var responseInactive = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest("bob@example.com"));

        // Assert: Still identical response and no reset token emitted
        responseInactive.Should().NotBeNull();
        responseInactive.Message.Should().Be(responseNonexistent.Message);

        var bobInDb = await _dbContext.Users.FirstAsync(u => u.Email == "bob@example.com");
        bobInDb.PasswordResetTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_UpdatesPasswordAndRevokesAllActiveRefreshTokens()
    {
        // Arrange: Register, verify, and log in Alice
        await _authService.RegisterAsync(new RegisterRequest("Alice", "Smith", "alice@example.com", "OldPassword123!"));
        var regOutbox = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "UserRegisteredEvent");
        var regDoc = JsonDocument.Parse(regOutbox.Payload);
        await _authService.VerifyEmailAsync(regDoc.RootElement.GetProperty("VerificationToken").GetString()!);

        // Login twice to have 2 active refresh tokens
        await _authService.LoginAsync(new LoginRequest("alice@example.com", "OldPassword123!"));
        await _authService.LoginAsync(new LoginRequest("alice@example.com", "OldPassword123!"));

        var user = await _dbContext.Users.Include(u => u.RefreshTokens).FirstAsync(u => u.Email == "alice@example.com");
        user.RefreshTokens.Count.Should().Be(2);
        user.RefreshTokens.All(r => r.IsActive).Should().BeTrue();

        // Request forgot password
        await _authService.ForgotPasswordAsync(new ForgotPasswordRequest("alice@example.com"));
        var resetOutbox = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "PasswordResetRequestedEvent");
        var resetDoc = JsonDocument.Parse(resetOutbox.Payload);
        var resetToken = resetDoc.RootElement.GetProperty("ResetToken").GetString();

        // Act: Reset password
        var resetResult = await _authService.ResetPasswordAsync(new ResetPasswordRequest(resetToken!, "NewPassword456!"));

        // Assert
        resetResult.Should().NotBeNull();
        resetResult.Message.Should().Contain("successfully reset");

        // Verify password hash changed
        var updatedUser = await _dbContext.Users.Include(u => u.RefreshTokens).FirstAsync(u => u.Email == "alice@example.com");
        updatedUser.PasswordHash.Should().Be("hashed_NewPassword456!");
        updatedUser.PasswordResetTokenHash.Should().BeNull();
        updatedUser.PasswordResetTokenExpiresAtUtc.Should().BeNull();

        // Verify ALL prior refresh tokens are revoked immediately
        updatedUser.RefreshTokens.All(r => r.IsRevoked).Should().BeTrue();

        // Verify old password fails
        var actOldLogin = () => _authService.LoginAsync(new LoginRequest("alice@example.com", "OldPassword123!"));
        await actOldLogin.Should().ThrowAsync<UnauthorizedDomainException>();

        // Verify new password succeeds
        var newLoginResponse = await _authService.LoginAsync(new LoginRequest("alice@example.com", "NewPassword456!"));
        newLoginResponse.Should().NotBeNull();
        newLoginResponse.AccessToken.Should().Be("access_token_123");
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidOrExpiredToken_ThrowsInvalidTransactionException()
    {
        // Act: Invalid token
        var actInvalid = () => _authService.ResetPasswordAsync(new ResetPasswordRequest("invalid-token", "NewPassword456!"));
        await actInvalid.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*invalid or has expired*");

        // Arrange: User with expired reset token
        var user = new User
        {
            FirstName = "Charlie",
            LastName = "Brown",
            Email = "charlie@example.com",
            PasswordHash = "hashed_pwd",
            IsActive = true,
            EmailConfirmed = true,
            PasswordResetTokenHash = "somehash",
            PasswordResetTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act: Expired token
        var actExpired = () => _authService.ResetPasswordAsync(new ResetPasswordRequest("some-token", "NewPassword456!"));
        await actExpired.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*invalid or has expired*");
    }

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_RotatesTokenAndRevokesOldOne()
    {
        // Arrange
        await _authService.RegisterAsync(new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!"));
        var regOutbox = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "UserRegisteredEvent");
        var regDoc = JsonDocument.Parse(regOutbox.Payload);
        await _authService.VerifyEmailAsync(regDoc.RootElement.GetProperty("VerificationToken").GetString()!);

        var loginResponse = await _authService.LoginAsync(new LoginRequest("alice@example.com", "Password123!"));
        var initialRefreshToken = loginResponse.RefreshToken;

        // Act: Refresh token
        var refreshResponse = await _authService.RefreshTokenAsync(new RefreshTokenRequest(initialRefreshToken));

        // Assert
        refreshResponse.Should().NotBeNull();
        refreshResponse.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Verify old token is now revoked in DB
        var oldTokenHash = $"hashed_{initialRefreshToken}";
        var oldToken = await _dbContext.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == oldTokenHash);
        oldToken.Should().NotBeNull();
        oldToken!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshTokenAsync_ReusedRevokedToken_ThrowsUnauthorizedDomainException()
    {
        // Arrange
        await _authService.RegisterAsync(new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!"));
        var regOutbox = await _dbContext.OutboxMessages.FirstAsync(m => m.Type == "UserRegisteredEvent");
        var regDoc = JsonDocument.Parse(regOutbox.Payload);
        await _authService.VerifyEmailAsync(regDoc.RootElement.GetProperty("VerificationToken").GetString()!);

        var loginResponse = await _authService.LoginAsync(new LoginRequest("alice@example.com", "Password123!"));
        var initialRefreshToken = loginResponse.RefreshToken;

        // First refresh succeeds
        await _authService.RefreshTokenAsync(new RefreshTokenRequest(initialRefreshToken));

        // Act: Attempt to reuse old initial refresh token
        var act = () => _authService.RefreshTokenAsync(new RefreshTokenRequest(initialRefreshToken));

        // Assert
        await act.Should().ThrowAsync<UnauthorizedDomainException>()
            .WithMessage("*Invalid or expired refresh token*");
    }

    [Fact]
    public async Task LoginAsync_WhenTwoFactorEnabled_ReturnsRequiresTwoFactorChallenge()
    {
        // Arrange
        _jwtTokenGeneratorMock.Setup(j => j.GenerateTwoFactorChallengeToken(It.IsAny<User>()))
            .Returns("two_factor_challenge_ticket");

        var user = new User
        {
            FirstName = "Bob",
            LastName = "Taylor",
            Email = "bob@example.com",
            PasswordHash = "hashed_Password123!",
            TwoFactorEnabled = true,
            IsActive = true,
            EmailConfirmed = true
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var loginRequest = new LoginRequest("bob@example.com", "Password123!");

        // Act
        var response = await _authService.LoginAsync(loginRequest);

        // Assert
        response.Should().NotBeNull();
        response.RequiresTwoFactor.Should().BeTrue();
        response.TwoFactorToken.Should().Be("two_factor_challenge_ticket");
        response.AccessToken.Should().BeEmpty();
        response.User.Should().BeNull();
    }
}
