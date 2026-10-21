namespace PayFlow.Domain.Entities;

public class WebhookDeliveryLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WebhookSubscriptionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public int AttemptCount { get; set; } = 1;
    public int? HttpStatusCode { get; set; }
    public long DurationMs { get; set; } = 0;
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
    public bool IsSuccess { get; set; } = false;
    public DateTime? NextRetryAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAtUtc { get; set; }

    public WebhookSubscription? WebhookSubscription { get; set; }
}
