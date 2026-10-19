using FluentAssertions;
using PayFlow.Application.DTOs;
using PayFlow.Application.Validators;
using Xunit;

namespace PayFlow.UnitTests.Validators;

public class PasswordResetValidatorTests
{
    private readonly ForgotPasswordRequestValidator _forgotPasswordValidator = new();
    private readonly ResetPasswordRequestValidator _resetPasswordValidator = new();

    [Fact]
    public void ForgotPassword_ValidEmail_PassesValidation()
    {
        var request = new ForgotPasswordRequest("user@example.com");
        var result = _forgotPasswordValidator.Validate(request);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Email is required.")]
    [InlineData("invalid-email", "A valid email address is required.")]
    public void ForgotPassword_InvalidEmail_ReturnsError(string email, string expectedError)
    {
        var request = new ForgotPasswordRequest(email);
        var result = _forgotPasswordValidator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains(expectedError));
    }

    [Fact]
    public void ResetPassword_ValidRequest_PassesValidation()
    {
        var request = new ResetPasswordRequest("valid-token-123", "StrongPassword123!");
        var result = _resetPasswordValidator.Validate(request);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "StrongPassword123!", "Reset token is required.")]
    [InlineData("token", "", "Password is required.")]
    [InlineData("token", "short1!", "Password must be at least 8 characters long.")]
    [InlineData("token", "alllowercase1!", "Password must contain at least one uppercase letter.")]
    [InlineData("token", "ALLUPPERCASE1!", "Password must contain at least one lowercase letter.")]
    [InlineData("token", "NoDigitsHere!!", "Password must contain at least one digit.")]
    [InlineData("token", "NoSpecialChars12", "Password must contain at least one special character.")]
    public void ResetPassword_InvalidRequest_ReturnsError(string token, string password, string expectedError)
    {
        var request = new ResetPasswordRequest(token, password);
        var result = _resetPasswordValidator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains(expectedError));
    }
}
