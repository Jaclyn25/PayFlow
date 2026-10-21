namespace PayFlow.Application.DTOs;

public record CreateWebhookSubscriptionRequest(
    string Url,
    IReadOnlyList<string> Events,
    string? Description = null
);

public record UpdateWebhookSubscriptionRequest(
    string? Url = null,
    IReadOnlyList<string>? Events = null,
    bool? IsActive = null,
    string? Description = null
);

public record WebhookSubscriptionResponse(
    Guid Id,
    Guid UserId,
    string Url,
    string SecretKey,
    string? Description,
    IReadOnlyList<string> Events,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

public record WebhookDeliveryLogDto(
    Guid Id,
    Guid WebhookSubscriptionId,
    string EventType,
    string Payload,
    string Signature,
    int AttemptCount,
    int? HttpStatusCode,
    long DurationMs,
    string? ResponseBody,
    string? Error,
    bool IsSuccess,
    DateTime? NextRetryAtUtc,
    DateTime CreatedAtUtc,
    DateTime? DeliveredAtUtc
);

public record WebhookTestPingResult(
    bool Success,
    int? HttpStatusCode,
    long DurationMs,
    string Message
);

public record WebhookPayloadDto(
    string Id,
    string Type,
    DateTime CreatedAt,
    object Data
);
