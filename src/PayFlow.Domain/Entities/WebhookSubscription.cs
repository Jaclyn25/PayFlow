namespace PayFlow.Domain.Entities;

public class WebhookSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EventsList { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<WebhookDeliveryLog> DeliveryLogs { get; set; } = new List<WebhookDeliveryLog>();

    public bool SubscribesTo(string eventType)
    {
        if (string.IsNullOrWhiteSpace(EventsList))
        {
            return false;
        }

        var events = EventsList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return events.Contains("*") || events.Contains(eventType, StringComparer.OrdinalIgnoreCase);
    }
}
