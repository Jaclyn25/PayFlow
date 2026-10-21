using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class WebhookSubscriptionService : IWebhookSubscriptionService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly IWebhookDispatcherService _dispatcherService;
    private readonly IWebhookSigner _signer;

    public WebhookSubscriptionService(
        IPayFlowDbContext dbContext,
        IWebhookDispatcherService dispatcherService,
        IWebhookSigner signer)
    {
        _dbContext = dbContext;
        _dispatcherService = dispatcherService;
        _signer = signer;
    }

    public async Task<WebhookSubscriptionResponse> CreateSubscriptionAsync(
        Guid userId,
        CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var secretKey = GenerateSecretKey();
        var normalizedEvents = string.Join(",", request.Events.Select(e => e.Trim().ToLowerInvariant()));

        var subscription = new WebhookSubscription
        {
            UserId = userId,
            Url = request.Url.Trim(),
            SecretKey = secretKey,
            Description = request.Description?.Trim(),
            EventsList = normalizedEvents,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.WebhookSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(subscription);
    }

    public async Task<IReadOnlyList<WebhookSubscriptionResponse>> GetSubscriptionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await _dbContext.WebhookSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return subscriptions.Select(MapToResponse).ToList();
    }

    public async Task<WebhookSubscriptionResponse> GetSubscriptionByIdAsync(
        Guid userId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.WebhookSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, cancellationToken);

        if (subscription == null)
        {
            throw new WebhookSubscriptionNotFoundException(subscriptionId);
        }

        return MapToResponse(subscription);
    }

    public async Task<WebhookSubscriptionResponse> UpdateSubscriptionAsync(
        Guid userId,
        Guid subscriptionId,
        UpdateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, cancellationToken);

        if (subscription == null)
        {
            throw new WebhookSubscriptionNotFoundException(subscriptionId);
        }

        if (request.Url != null)
        {
            subscription.Url = request.Url.Trim();
        }

        if (request.Events != null)
        {
            subscription.EventsList = string.Join(",", request.Events.Select(e => e.Trim().ToLowerInvariant()));
        }

        if (request.IsActive.HasValue)
        {
            subscription.IsActive = request.IsActive.Value;
        }

        if (request.Description != null)
        {
            subscription.Description = request.Description.Trim();
        }

        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(subscription);
    }

    public async Task DeleteSubscriptionAsync(
        Guid userId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, cancellationToken);

        if (subscription == null)
        {
            throw new WebhookSubscriptionNotFoundException(subscriptionId);
        }

        _dbContext.WebhookSubscriptions.Remove(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WebhookSubscriptionResponse> RotateSecretAsync(
        Guid userId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, cancellationToken);

        if (subscription == null)
        {
            throw new WebhookSubscriptionNotFoundException(subscriptionId);
        }

        subscription.SecretKey = GenerateSecretKey();
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(subscription);
    }

    public async Task<PagedResult<WebhookDeliveryLogDto>> GetDeliveryLogsAsync(
        Guid userId,
        Guid subscriptionId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var subscriptionExists = await _dbContext.WebhookSubscriptions
            .AnyAsync(s => s.Id == subscriptionId && s.UserId == userId, cancellationToken);

        if (!subscriptionExists)
        {
            throw new WebhookSubscriptionNotFoundException(subscriptionId);
        }

        var validPage = page < 1 ? 1 : page;
        var validPageSize = pageSize is < 1 or > 100 ? 20 : pageSize;

        var query = _dbContext.WebhookDeliveryLogs
            .AsNoTracking()
            .Where(l => l.WebhookSubscriptionId == subscriptionId);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((validPage - 1) * validPageSize)
            .Take(validPageSize)
            .Select(l => new WebhookDeliveryLogDto(
                l.Id,
                l.WebhookSubscriptionId,
                l.EventType,
                l.Payload,
                l.Signature,
                l.AttemptCount,
                l.HttpStatusCode,
                l.DurationMs,
                l.ResponseBody,
                l.Error,
                l.IsSuccess,
                l.NextRetryAtUtc,
                l.CreatedAtUtc,
                l.DeliveredAtUtc
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<WebhookDeliveryLogDto>.Create(items, validPage, validPageSize, totalCount);
    }

    public async Task<WebhookDeliveryLogDto> RedeliverAsync(
        Guid userId,
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        var log = await _dbContext.WebhookDeliveryLogs
            .Include(l => l.WebhookSubscription)
            .FirstOrDefaultAsync(l => l.Id == deliveryId, cancellationToken);

        if (log == null || log.WebhookSubscription == null || log.WebhookSubscription.UserId != userId)
        {
            throw new WebhookDeliveryNotFoundException(deliveryId);
        }

        await _dispatcherService.DeliverSingleWebhookAsync(deliveryId, cancellationToken);

        var updatedLog = await _dbContext.WebhookDeliveryLogs
            .AsNoTracking()
            .FirstAsync(l => l.Id == deliveryId, cancellationToken);

        return new WebhookDeliveryLogDto(
            updatedLog.Id,
            updatedLog.WebhookSubscriptionId,
            updatedLog.EventType,
            updatedLog.Payload,
            updatedLog.Signature,
            updatedLog.AttemptCount,
            updatedLog.HttpStatusCode,
            updatedLog.DurationMs,
            updatedLog.ResponseBody,
            updatedLog.Error,
            updatedLog.IsSuccess,
            updatedLog.NextRetryAtUtc,
            updatedLog.CreatedAtUtc,
            updatedLog.DeliveredAtUtc
        );
    }

    public async Task<WebhookTestPingResult> TestPingAsync(
        Guid userId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, cancellationToken);

        if (subscription == null)
        {
            throw new WebhookSubscriptionNotFoundException(subscriptionId);
        }

        var payloadObj = new WebhookPayloadDto(
            Guid.NewGuid().ToString(),
            WebhookEventConstants.Ping,
            DateTime.UtcNow,
            new { message = "Ping event from PayFlow webhook system.", subscriptionId = subscription.Id }
        );

        var json = JsonSerializer.Serialize(payloadObj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var signature = _signer.ComputeSignature(json, subscription.SecretKey);

        var deliveryLog = new WebhookDeliveryLog
        {
            WebhookSubscriptionId = subscription.Id,
            EventType = WebhookEventConstants.Ping,
            Payload = json,
            Signature = signature,
            AttemptCount = 0,
            IsSuccess = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.WebhookDeliveryLogs.Add(deliveryLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var success = await _dispatcherService.DeliverSingleWebhookAsync(deliveryLog.Id, cancellationToken);

        var updatedLog = await _dbContext.WebhookDeliveryLogs
            .AsNoTracking()
            .FirstAsync(l => l.Id == deliveryLog.Id, cancellationToken);

        return new WebhookTestPingResult(
            updatedLog.IsSuccess,
            updatedLog.HttpStatusCode,
            updatedLog.DurationMs,
            updatedLog.IsSuccess ? "Test ping delivered successfully." : (updatedLog.Error ?? "Delivery failed")
        );
    }

    private static string GenerateSecretKey()
    {
        return "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    }

    private static WebhookSubscriptionResponse MapToResponse(WebhookSubscription sub)
    {
        var events = sub.EventsList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new WebhookSubscriptionResponse(
            sub.Id,
            sub.UserId,
            sub.Url,
            sub.SecretKey,
            sub.Description,
            events,
            sub.IsActive,
            sub.CreatedAtUtc,
            sub.UpdatedAtUtc
        );
    }
}
