using FluentAssertions;
using PayFlow.Application.DTOs;
using PayFlow.Application.Validators;
using PayFlow.Domain.Constants;

namespace PayFlow.UnitTests.Validators;

public class WebhookValidatorsTests
{
    private readonly CreateWebhookSubscriptionRequestValidator _createValidator = new();
    private readonly UpdateWebhookSubscriptionRequestValidator _updateValidator = new();

    [Theory]
    [InlineData("https://api.merchant.com/webhook")]
    [InlineData("http://localhost:5000/webhook")]
    public void CreateValidator_WithValidUrlAndEvents_PassesValidation(string url)
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest(
            Url: url,
            Events: new[] { WebhookEventConstants.TransferCompleted },
            Description: "Valid endpoint"
        );

        // Act
        var result = _createValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-url")]
    [InlineData("ftp://invalid-scheme.com/webhook")]
    public void CreateValidator_WithInvalidUrl_FailsValidation(string url)
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest(
            Url: url,
            Events: new[] { "*" }
        );

        // Act
        var result = _createValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Url");
    }

    [Fact]
    public void CreateValidator_WithEmptyEvents_FailsValidation()
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest(
            Url: "https://valid.com/hook",
            Events: Array.Empty<string>()
        );

        // Act
        var result = _createValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Events");
    }

    [Fact]
    public void CreateValidator_WithUnsupportedEvents_FailsValidation()
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest(
            Url: "https://valid.com/hook",
            Events: new[] { "invalid.event.name" }
        );

        // Act
        var result = _createValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Events");
    }

    [Fact]
    public void UpdateValidator_WithNullOptionalFields_PassesValidation()
    {
        // Arrange
        var request = new UpdateWebhookSubscriptionRequest(
            Url: null,
            Events: null,
            IsActive: false,
            Description: null
        );

        // Act
        var result = _updateValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateValidator_WithInvalidUrl_FailsValidation()
    {
        // Arrange
        var request = new UpdateWebhookSubscriptionRequest(
            Url: "invalid-url"
        );

        // Act
        var result = _updateValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Url");
    }
}
