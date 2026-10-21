using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Security;

namespace PayFlow.UnitTests.Services;

public class WebhookSubscriptionServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<IWebhookDispatcherService> _dispatcherMock = new();
    private readonly IWebhookSigner _signer = new WebhookSigner();
    private readonly WebhookSubscriptionService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public WebhookSubscriptionServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(dbOptions);
        _service = new WebhookSubscriptionService(_dbContext, _dispatcherMock.Object, _signer);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_CreatesSubscriptionWithGeneratedSecret()
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest(
            Url: "https://api.example.com/webhooks",
            Events: new[] { WebhookEventConstants.TransferCompleted, WebhookEventConstants.DepositSucceeded },
            Description: "Primary endpoint"
        );

        // Act
        var result = await _service.CreateSubscriptionAsync(_userId, request);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(_userId);
        result.Url.Should().Be("https://api.example.com/webhooks");
        result.SecretKey.Should().StartWith("whsec_");
        result.SecretKey.Length.Should().BeGreaterThan(20);
        result.Events.Should().BeEquivalentTo(new[] { WebhookEventConstants.TransferCompleted, WebhookEventConstants.DepositSucceeded });
        result.IsActive.Should().BeTrue();
        result.Description.Should().Be("Primary endpoint");

        var stored = await _dbContext.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == result.Id);
        stored.Should().NotBeNull();
        stored!.SecretKey.Should().Be(result.SecretKey);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsOnlyUserSubscriptions()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://user1.com/hook", new[] { "*" }));
        await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://user1-second.com/hook", new[] { WebhookEventConstants.TransferCompleted }));
        await _service.CreateSubscriptionAsync(otherUserId, new CreateWebhookSubscriptionRequest("https://other.com/hook", new[] { "*" }));

        // Act
        var userSubs = await _service.GetSubscriptionsAsync(_userId);

        // Assert
        userSubs.Should().HaveCount(2);
        userSubs.All(s => s.UserId == _userId).Should().BeTrue();
    }

    [Fact]
    public async Task GetSubscriptionByIdAsync_WhenFound_ReturnsSubscription()
    {
        // Arrange
        var created = await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "*" }));

        // Act
        var result = await _service.GetSubscriptionByIdAsync(_userId, created.Id);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetSubscriptionByIdAsync_WhenNotFound_ThrowsException()
    {
        // Act & Assert
        var act = async () => await _service.GetSubscriptionByIdAsync(_userId, Guid.NewGuid());
        await act.Should().ThrowAsync<WebhookSubscriptionNotFoundException>();
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_UpdatesSpecifiedFields()
    {
        // Arrange
        var created = await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://old.com/hook", new[] { "transfer.completed" }));

        var updateRequest = new UpdateWebhookSubscriptionRequest(
            Url: "https://new.com/hook",
            Events: new[] { "deposit.succeeded" },
            IsActive: false,
            Description: "Updated description"
        );

        // Act
        var updated = await _service.UpdateSubscriptionAsync(_userId, created.Id, updateRequest);

        // Assert
        updated.Url.Should().Be("https://new.com/hook");
        updated.Events.Should().BeEquivalentTo(new[] { "deposit.succeeded" });
        updated.IsActive.Should().BeFalse();
        updated.Description.Should().Be("Updated description");
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_RemovesFromDatabase()
    {
        // Arrange
        var created = await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://delete.com/hook", new[] { "*" }));

        // Act
        await _service.DeleteSubscriptionAsync(_userId, created.Id);

        // Assert
        var exists = await _dbContext.WebhookSubscriptions.AnyAsync(s => s.Id == created.Id);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task RotateSecretAsync_GeneratesNewSecretKey()
    {
        // Arrange
        var created = await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://rotate.com/hook", new[] { "*" }));
        var oldKey = created.SecretKey;

        // Act
        var rotated = await _service.RotateSecretAsync(_userId, created.Id);

        // Assert
        rotated.SecretKey.Should().NotBe(oldKey);
        rotated.SecretKey.Should().StartWith("whsec_");
    }

    [Fact]
    public async Task GetDeliveryLogsAsync_ReturnsPagedLogs()
    {
        // Arrange
        var created = await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://logs.com/hook", new[] { "*" }));

        for (int i = 0; i < 5; i++)
        {
            _dbContext.WebhookDeliveryLogs.Add(new WebhookDeliveryLog
            {
                WebhookSubscriptionId = created.Id,
                EventType = WebhookEventConstants.TransferCompleted,
                Payload = "{}",
                Signature = "sig",
                AttemptCount = 1,
                IsSuccess = true,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var paged = await _service.GetDeliveryLogsAsync(_userId, created.Id, page: 1, pageSize: 3);

        // Assert
        paged.Items.Should().HaveCount(3);
        paged.TotalCount.Should().Be(5);
        paged.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task RedeliverAsync_DispatchesSingleWebhook()
    {
        // Arrange
        var created = await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://redeliver.com/hook", new[] { "*" }));

        var log = new WebhookDeliveryLog
        {
            WebhookSubscriptionId = created.Id,
            EventType = WebhookEventConstants.TransferCompleted,
            Payload = "{}",
            Signature = "sig",
            AttemptCount = 1,
            IsSuccess = false
        };
        _dbContext.WebhookDeliveryLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        _dispatcherMock
            .Setup(d => d.DeliverSingleWebhookAsync(log.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RedeliverAsync(_userId, log.Id);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(log.Id);
        _dispatcherMock.Verify(d => d.DeliverSingleWebhookAsync(log.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TestPingAsync_CreatesPingLogAndDispatchesDelivery()
    {
        // Arrange
        var created = await _service.CreateSubscriptionAsync(_userId, new CreateWebhookSubscriptionRequest("https://testping.com/hook", new[] { "*" }));

        _dispatcherMock
            .Setup(d => d.DeliverSingleWebhookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((logId, ct) =>
            {
                var delivery = _dbContext.WebhookDeliveryLogs.First(l => l.Id == logId);
                delivery.IsSuccess = true;
                delivery.HttpStatusCode = 200;
                delivery.DurationMs = 45;
                _dbContext.SaveChanges();
            })
            .ReturnsAsync(true);

        // Act
        var result = await _service.TestPingAsync(_userId, created.Id);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.HttpStatusCode.Should().Be(200);
        result.DurationMs.Should().Be(45);

        var log = await _dbContext.WebhookDeliveryLogs.FirstOrDefaultAsync(l => l.WebhookSubscriptionId == created.Id && l.EventType == WebhookEventConstants.Ping);
        log.Should().NotBeNull();
    }
}
