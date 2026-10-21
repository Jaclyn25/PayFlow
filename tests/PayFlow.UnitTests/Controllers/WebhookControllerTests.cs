using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PayFlow.Api.Controllers;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Validators;
using PayFlow.Domain.Constants;

namespace PayFlow.UnitTests.Controllers;

public class WebhookControllerTests
{
    private readonly Mock<IWebhookSubscriptionService> _subscriptionServiceMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly WebhookController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public WebhookControllerTests()
    {
        _currentUserServiceMock.Setup(s => s.UserId).Returns(_userId);
        _controller = new WebhookController(_subscriptionServiceMock.Object, _currentUserServiceMock.Object);
    }

    [Fact]
    public async Task CreateSubscription_WithValidRequest_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "*" }, "Desc");
        var validator = new CreateWebhookSubscriptionRequestValidator();

        var expectedResponse = new WebhookSubscriptionResponse(
            Guid.NewGuid(),
            _userId,
            request.Url,
            "whsec_12345",
            request.Description,
            request.Events,
            true,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

        _subscriptionServiceMock
            .Setup(s => s.CreateSubscriptionAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.CreateSubscription(request, validator, CancellationToken.None);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(201);
        createdResult.Value.Should().BeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task CreateSubscription_WithInvalidRequest_ThrowsValidationException()
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest("invalid-url", Array.Empty<string>());
        var validator = new CreateWebhookSubscriptionRequestValidator();

        // Act & Assert
        var act = async () => await _controller.CreateSubscription(request, validator, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsOkWithList()
    {
        // Arrange
        var list = new List<WebhookSubscriptionResponse>
        {
            new(Guid.NewGuid(), _userId, "https://hook1.com", "whsec_1", null, new[] { "*" }, true, DateTime.UtcNow, DateTime.UtcNow)
        };

        _subscriptionServiceMock
            .Setup(s => s.GetSubscriptionsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        // Act
        var result = await _controller.GetSubscriptions(CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeEquivalentTo(list);
    }

    [Fact]
    public async Task DeleteSubscription_ReturnsNoContent()
    {
        // Arrange
        var subId = Guid.NewGuid();
        _subscriptionServiceMock
            .Setup(s => s.DeleteSubscriptionAsync(_userId, subId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.DeleteSubscription(subId, CancellationToken.None);

        // Assert
        var noContent = result as NoContentResult;
        noContent.Should().NotBeNull();
    }

    [Fact]
    public async Task RotateSecret_ReturnsOkWithUpdatedSecret()
    {
        // Arrange
        var subId = Guid.NewGuid();
        var response = new WebhookSubscriptionResponse(
            subId, _userId, "https://hook.com", "whsec_newsecret", null, new[] { "*" }, true, DateTime.UtcNow, DateTime.UtcNow
        );

        _subscriptionServiceMock
            .Setup(s => s.RotateSecretAsync(_userId, subId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.RotateSecret(subId, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().Be(response);
    }

    [Fact]
    public async Task TestPing_ReturnsOkWithResult()
    {
        // Arrange
        var subId = Guid.NewGuid();
        var pingResult = new WebhookTestPingResult(true, 200, 35, "Ping delivered successfully.");

        _subscriptionServiceMock
            .Setup(s => s.TestPingAsync(_userId, subId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pingResult);

        // Act
        var result = await _controller.TestPing(subId, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().Be(pingResult);
    }
}
