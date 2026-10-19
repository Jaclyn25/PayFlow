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
            .Returns("hashed_password");

        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((pwd, hash) => pwd == "Password123!" && hash == "hashed_password");

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
    public async Task RegisterAsync_NewUser_CreatesUserWalletAndTokens()
    {
        // Arrange
        var request = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");

        // Act
        var response = await _authService.RegisterAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.AccessToken.Should().Be("access_token_123");
        response.RefreshToken.Should().Be("raw_refresh_token_123");
        response.User!.Email.Should().Be("alice@example.com");

        var userInDb = await _dbContext.Users.Include(u => u.Wallet).FirstOrDefaultAsync(u => u.Email == "alice@example.com");
        userInDb.Should().NotBeNull();
        userInDb!.Wallet.Should().NotBeNull();
        userInDb.Wallet!.Balance.Should().Be(0.00m);
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
    public async Task LoginAsync_ValidCredentials_ReturnsAuthResponse()
    {
        // Arrange
        var registerRequest = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(registerRequest);

        var loginRequest = new LoginRequest("alice@example.com", "Password123!");

        // Act
        var response = await _authService.LoginAsync(loginRequest);

        // Assert
        response.Should().NotBeNull();
        response.AccessToken.Should().Be("access_token_123");
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ThrowsUnauthorizedDomainException()
    {
        // Arrange
        var registerRequest = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");
        await _authService.RegisterAsync(registerRequest);

        var loginRequest = new LoginRequest("alice@example.com", "WrongPassword!");

        // Act
        var act = () => _authService.LoginAsync(loginRequest);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedDomainException>()
            .WithMessage("*Invalid email or password*");
    }

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_RotatesTokenAndRevokesOldOne()
    {
        // Arrange
        var registerResponse = await _authService.RegisterAsync(new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!"));
        var initialRefreshToken = registerResponse.RefreshToken;

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
        var registerResponse = await _authService.RegisterAsync(new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!"));
        var initialRefreshToken = registerResponse.RefreshToken;

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
            PasswordHash = "hashed_password",
            TwoFactorEnabled = true
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

