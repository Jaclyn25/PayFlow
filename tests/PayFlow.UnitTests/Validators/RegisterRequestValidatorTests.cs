using FluentAssertions;
using PayFlow.Application.DTOs;
using PayFlow.Application.Validators;
using Xunit;

namespace PayFlow.UnitTests.Validators;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_PassesValidation()
    {
        // Arrange
        var request = new RegisterRequest("Alice", "Smith", "alice@example.com", "Password123!");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Smith", "alice@example.com", "Password123!", "First name is required.")]
    [InlineData("Alice", "", "alice@example.com", "Password123!", "Last name is required.")]
    [InlineData("Alice", "Smith", "invalid-email", "Password123!", "A valid email address is required.")]
    [InlineData("Alice", "Smith", "alice@example.com", "short", "Password must be at least 8 characters long.")]
    [InlineData("Alice", "Smith", "alice@example.com", "nopassword123!", "Password must contain at least one uppercase letter.")]
    public void Validate_InvalidRequest_ReturnsErrors(string firstName, string lastName, string email, string password, string expectedErrorMessage)
    {
        // Arrange
        var request = new RegisterRequest(firstName, lastName, email, password);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains(expectedErrorMessage));
    }
}
