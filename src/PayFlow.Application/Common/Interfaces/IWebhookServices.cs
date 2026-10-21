using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IWebhookSubscriptionService
{
    Task<WebhookSubscriptionResponse> CreateSubscriptionAsync(Guid userId, CreateWebhookSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookSubscriptionResponse>> GetSubscriptionsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<WebhookSubscriptionResponse> GetSubscriptionByIdAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<WebhookSubscriptionResponse> UpdateSubscriptionAsync(Guid userId, Guid subscriptionId, UpdateWebhookSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task DeleteSubscriptionAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<WebhookSubscriptionResponse> RotateSecretAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<PagedResult<WebhookDeliveryLogDto>> GetDeliveryLogsAsync(Guid userId, Guid subscriptionId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<WebhookDeliveryLogDto> RedeliverAsync(Guid userId, Guid deliveryId, CancellationToken cancellationToken = default);
    Task<WebhookTestPingResult> TestPingAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default);
}

public interface IWebhookDispatcherService
{
    Task DispatchEventAsync(string eventType, object eventData, Guid? targetUserId = null, CancellationToken cancellationToken = default);
    Task<bool> DeliverSingleWebhookAsync(Guid deliveryLogId, CancellationToken cancellationToken = default);
    Task ProcessPendingRetriesAsync(CancellationToken cancellationToken = default);
}

public interface IWebhookSigner
{
    string ComputeSignature(string payload, string secretKey);
    bool VerifySignature(string payload, string secretKey, string signature);
}
