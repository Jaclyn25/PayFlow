using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Messaging;

public class WebhookDispatcherService : IWebhookDispatcherService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebhookSigner _signer;
    private readonly ILogger<WebhookDispatcherService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookDispatcherService(
        IPayFlowDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IWebhookSigner signer,
        ILogger<WebhookDispatcherService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _signer = signer;
        _logger = logger;
    }

    public async Task DispatchEventAsync(
        string eventType,
        object eventData,
        Guid? targetUserId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.WebhookSubscriptions
                .AsNoTracking()
                .Where(s => s.IsActive);

            if (targetUserId.HasValue)
            {
                query = query.Where(s => s.UserId == targetUserId.Value);
            }

            var subscriptions = await query.ToListAsync(cancellationToken);
            var matchingSubscriptions = subscriptions
                .Where(s => s.SubscribesTo(eventType))
                .ToList();

            if (matchingSubscriptions.Count == 0)
            {
                _logger.LogDebug("[WEBHOOK DISPATCHER] No active subscriptions found for event '{EventType}'.", eventType);
                return;
            }

            var createdLogs = new List<WebhookDeliveryLog>();

            foreach (var subscription in matchingSubscriptions)
            {
                var payloadEnvelope = new WebhookPayloadDto(
                    Guid.NewGuid().ToString(),
                    eventType,
                    DateTime.UtcNow,
                    eventData
                );

                var jsonPayload = JsonSerializer.Serialize(payloadEnvelope, JsonOptions);
                var signature = _signer.ComputeSignature(jsonPayload, subscription.SecretKey);

                var deliveryLog = new WebhookDeliveryLog
                {
                    WebhookSubscriptionId = subscription.Id,
                    EventType = eventType,
                    Payload = jsonPayload,
                    Signature = signature,
                    AttemptCount = 0,
                    IsSuccess = false,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.WebhookDeliveryLogs.Add(deliveryLog);
                createdLogs.Add(deliveryLog);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Attempt immediate delivery for each log
            foreach (var log in createdLogs)
            {
                try
                {
                    await DeliverSingleWebhookAsync(log.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[WEBHOOK DISPATCHER] Immediate delivery attempt failed for log '{LogId}'. Will retry later.", log.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WEBHOOK DISPATCHER] Failed to dispatch event '{EventType}'.", eventType);
        }
    }

    public async Task<bool> DeliverSingleWebhookAsync(
        Guid deliveryLogId,
        CancellationToken cancellationToken = default)
    {
        var log = await _dbContext.WebhookDeliveryLogs
            .Include(l => l.WebhookSubscription)
            .FirstOrDefaultAsync(l => l.Id == deliveryLogId, cancellationToken);

        if (log == null || log.WebhookSubscription == null)
        {
            _logger.LogWarning("[WEBHOOK DISPATCHER] Delivery log '{DeliveryLogId}' or associated subscription not found.", deliveryLogId);
            return false;
        }

        var subscription = log.WebhookSubscription;
        log.AttemptCount++;

        var client = _httpClientFactory.CreateClient("Webhooks");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = new StringContent(log.Payload, Encoding.UTF8, "application/json")
            };

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            request.Headers.Add("X-PayFlow-Signature", $"sha256={log.Signature}");
            request.Headers.Add("X-PayFlow-Event", log.EventType);
            request.Headers.Add("X-PayFlow-Delivery", log.Id.ToString());
            request.Headers.Add("X-PayFlow-Timestamp", timestamp);
            request.Headers.Add("User-Agent", "PayFlow-Webhook-Engine/1.0");

            var response = await client.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            log.DurationMs = stopwatch.ElapsedMilliseconds;
            log.HttpStatusCode = (int)response.StatusCode;

            var bodySnippet = await response.Content.ReadAsStringAsync(cancellationToken);
            log.ResponseBody = bodySnippet.Length > 1000 ? bodySnippet[..1000] : bodySnippet;

            if (response.IsSuccessStatusCode)
            {
                log.IsSuccess = true;
                log.DeliveredAtUtc = DateTime.UtcNow;
                log.NextRetryAtUtc = null;
                log.Error = null;

                _logger.LogInformation("[WEBHOOK DISPATCHER] Successfully delivered webhook '{DeliveryLogId}' to '{Url}' (Status: {StatusCode}, {DurationMs}ms).",
                    log.Id, subscription.Url, log.HttpStatusCode, log.DurationMs);
            }
            else
            {
                log.IsSuccess = false;
                log.Error = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                ScheduleNextRetry(log);

                _logger.LogWarning("[WEBHOOK DISPATCHER] Webhook '{DeliveryLogId}' failed with HTTP {StatusCode}. Next retry: {NextRetry}.",
                    log.Id, log.HttpStatusCode, log.NextRetryAtUtc);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            log.DurationMs = stopwatch.ElapsedMilliseconds;
            log.IsSuccess = false;
            log.Error = ex.Message;
            ScheduleNextRetry(log);

            _logger.LogWarning(ex, "[WEBHOOK DISPATCHER] Exception delivering webhook '{DeliveryLogId}' to '{Url}'. Next retry: {NextRetry}.",
                log.Id, subscription.Url, log.NextRetryAtUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return log.IsSuccess;
    }

    public async Task ProcessPendingRetriesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var pendingLogIds = await _dbContext.WebhookDeliveryLogs
            .Where(l => !l.IsSuccess && l.NextRetryAtUtc != null && l.NextRetryAtUtc <= now && l.AttemptCount < 5)
            .OrderBy(l => l.NextRetryAtUtc)
            .Take(50)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        if (pendingLogIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("[WEBHOOK DISPATCHER] Found {Count} pending webhook deliveries to retry.", pendingLogIds.Count);

        foreach (var logId in pendingLogIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await DeliverSingleWebhookAsync(logId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WEBHOOK DISPATCHER] Failed processing retry for delivery log '{LogId}'.", logId);
            }
        }
    }

    private static void ScheduleNextRetry(WebhookDeliveryLog log)
    {
        if (log.AttemptCount >= 5)
        {
            log.NextRetryAtUtc = null;
            return;
        }

        var delay = log.AttemptCount switch
        {
            1 => TimeSpan.FromSeconds(15),
            2 => TimeSpan.FromMinutes(1),
            3 => TimeSpan.FromMinutes(5),
            4 => TimeSpan.FromMinutes(15),
            _ => (TimeSpan?)null
        };

        log.NextRetryAtUtc = delay.HasValue ? DateTime.UtcNow.Add(delay.Value) : null;
    }
}
