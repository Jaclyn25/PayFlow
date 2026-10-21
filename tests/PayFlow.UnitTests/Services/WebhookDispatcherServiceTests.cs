using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Infrastructure.Messaging;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Security;

namespace PayFlow.UnitTests.Services;

public class WebhookDispatcherServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly IWebhookSigner _signer = new WebhookSigner();
    private readonly Mock<ILogger<WebhookDispatcherService>> _loggerMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    public WebhookDispatcherServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(dbOptions);
    }

    [Fact]
    public async Task DispatchEventAsync_CreatesDeliveryLogsForMatchingSubscriptions()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((req) => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient("Webhooks")).Returns(client);

        var subMatching = new WebhookSubscription
        {
            UserId = _userId,
            Url = "https://endpoint.com/hook",
            SecretKey = "whsec_testkey",
            EventsList = "transfer.completed,deposit.succeeded",
            IsActive = true
        };

        var subWildcard = new WebhookSubscription
        {
            UserId = _userId,
            Url = "https://wildcard.com/hook",
            SecretKey = "whsec_wildkey",
            EventsList = "*",
            IsActive = true
        };

        var subNonMatching = new WebhookSubscription
        {
            UserId = _userId,
            Url = "https://ignored.com/hook",
            SecretKey = "whsec_otherkey",
            EventsList = "deposit.succeeded",
            IsActive = true
        };

        var subInactive = new WebhookSubscription
        {
            UserId = _userId,
            Url = "https://inactive.com/hook",
            SecretKey = "whsec_inactkey",
            EventsList = "*",
            IsActive = false
        };

        _dbContext.WebhookSubscriptions.AddRange(subMatching, subWildcard, subNonMatching, subInactive);
        await _dbContext.SaveChangesAsync();

        var service = new WebhookDispatcherService(_dbContext, _httpClientFactoryMock.Object, _signer, _loggerMock.Object);

        // Act
        await service.DispatchEventAsync(WebhookEventConstants.TransferCompleted, new { Amount = 250m }, _userId);

        // Assert
        var logs = await _dbContext.WebhookDeliveryLogs.ToListAsync();
        logs.Should().HaveCount(2, "only matching active subscriptions should receive deliveries");
        logs.Should().Contain(l => l.WebhookSubscriptionId == subMatching.Id);
        logs.Should().Contain(l => l.WebhookSubscriptionId == subWildcard.Id);
        logs.Should().NotContain(l => l.WebhookSubscriptionId == subNonMatching.Id);
        logs.Should().NotContain(l => l.WebhookSubscriptionId == subInactive.Id);

        var matchingLog = logs.First(l => l.WebhookSubscriptionId == subMatching.Id);
        matchingLog.Signature.Should().NotBeNullOrWhiteSpace();
        matchingLog.IsSuccess.Should().BeTrue();
        matchingLog.HttpStatusCode.Should().Be(200);

        // Verify HMAC-SHA256 signature verification
        var isSignatureValid = _signer.VerifySignature(matchingLog.Payload, subMatching.SecretKey, matchingLog.Signature);
        isSignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task DeliverSingleWebhookAsync_SendsHeadersAndRecordsLatency()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler((req) =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("{\"status\":\"ok\"}")
            };
        });

        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient("Webhooks")).Returns(client);

        var sub = new WebhookSubscription
        {
            UserId = _userId,
            Url = "https://merchant.example.com/api/webhooks",
            SecretKey = "whsec_123456789",
            EventsList = "*",
            IsActive = true
        };
        _dbContext.WebhookSubscriptions.Add(sub);

        var log = new WebhookDeliveryLog
        {
            WebhookSubscriptionId = sub.Id,
            EventType = WebhookEventConstants.TransferCompleted,
            Payload = "{\"event\":\"transfer.completed\"}",
            Signature = _signer.ComputeSignature("{\"event\":\"transfer.completed\"}", sub.SecretKey),
            AttemptCount = 0,
            IsSuccess = false
        };
        _dbContext.WebhookDeliveryLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        var service = new WebhookDispatcherService(_dbContext, _httpClientFactoryMock.Object, _signer, _loggerMock.Object);

        // Act
        var success = await service.DeliverSingleWebhookAsync(log.Id);

        // Assert
        success.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Contains("X-PayFlow-Signature").Should().BeTrue();
        capturedRequest.Headers.GetValues("X-PayFlow-Signature").First().Should().StartWith("sha256=");
        capturedRequest.Headers.Contains("X-PayFlow-Event").Should().BeTrue();
        capturedRequest.Headers.GetValues("X-PayFlow-Event").First().Should().Be(WebhookEventConstants.TransferCompleted);
        capturedRequest.Headers.Contains("X-PayFlow-Delivery").Should().BeTrue();
        capturedRequest.Headers.Contains("X-PayFlow-Timestamp").Should().BeTrue();
        capturedRequest.Headers.Contains("User-Agent").Should().BeTrue();

        var updated = await _dbContext.WebhookDeliveryLogs.FirstAsync(l => l.Id == log.Id);
        updated.IsSuccess.Should().BeTrue();
        updated.AttemptCount.Should().Be(1);
        updated.HttpStatusCode.Should().Be(202);
        updated.ResponseBody.Should().Contain("ok");
        updated.DeliveredAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task DeliverSingleWebhookAsync_OnFailure_SchedulesRetryWithBackoff()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((req) => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Internal Server Error"
        });

        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient("Webhooks")).Returns(client);

        var sub = new WebhookSubscription
        {
            UserId = _userId,
            Url = "https://failing.example.com/webhook",
            SecretKey = "whsec_key",
            EventsList = "*",
            IsActive = true
        };
        _dbContext.WebhookSubscriptions.Add(sub);

        var log = new WebhookDeliveryLog
        {
            WebhookSubscriptionId = sub.Id,
            EventType = WebhookEventConstants.TransferCompleted,
            Payload = "{}",
            Signature = "sig",
            AttemptCount = 0,
            IsSuccess = false
        };
        _dbContext.WebhookDeliveryLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        var service = new WebhookDispatcherService(_dbContext, _httpClientFactoryMock.Object, _signer, _loggerMock.Object);

        // Act
        var success = await service.DeliverSingleWebhookAsync(log.Id);

        // Assert
        success.Should().BeFalse();

        var updated = await _dbContext.WebhookDeliveryLogs.FirstAsync(l => l.Id == log.Id);
        updated.IsSuccess.Should().BeFalse();
        updated.AttemptCount.Should().Be(1);
        updated.HttpStatusCode.Should().Be(500);
        updated.NextRetryAtUtc.Should().NotBeNull();
        updated.NextRetryAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(10));
    }

    [Fact]
    public async Task ProcessPendingRetriesAsync_ExecutesDueRetries()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((req) => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient("Webhooks")).Returns(client);

        var sub = new WebhookSubscription
        {
            UserId = _userId,
            Url = "https://retry.example.com/webhook",
            SecretKey = "whsec_key",
            EventsList = "*",
            IsActive = true
        };
        _dbContext.WebhookSubscriptions.Add(sub);

        var dueLog = new WebhookDeliveryLog
        {
            WebhookSubscriptionId = sub.Id,
            EventType = WebhookEventConstants.TransferCompleted,
            Payload = "{}",
            Signature = "sig",
            AttemptCount = 1,
            IsSuccess = false,
            NextRetryAtUtc = DateTime.UtcNow.AddMinutes(-5) // overdue
        };

        var futureLog = new WebhookDeliveryLog
        {
            WebhookSubscriptionId = sub.Id,
            EventType = WebhookEventConstants.TransferCompleted,
            Payload = "{}",
            Signature = "sig",
            AttemptCount = 1,
            IsSuccess = false,
            NextRetryAtUtc = DateTime.UtcNow.AddMinutes(10) // not due
        };

        _dbContext.WebhookDeliveryLogs.AddRange(dueLog, futureLog);
        await _dbContext.SaveChangesAsync();

        var service = new WebhookDispatcherService(_dbContext, _httpClientFactoryMock.Object, _signer, _loggerMock.Object);

        // Act
        await service.ProcessPendingRetriesAsync();

        // Assert
        var updatedDue = await _dbContext.WebhookDeliveryLogs.FirstAsync(l => l.Id == dueLog.Id);
        updatedDue.IsSuccess.Should().BeTrue();
        updatedDue.AttemptCount.Should().Be(2);

        var updatedFuture = await _dbContext.WebhookDeliveryLogs.FirstAsync(l => l.Id == futureLog.Id);
        updatedFuture.IsSuccess.Should().BeFalse();
        updatedFuture.AttemptCount.Should().Be(1);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
