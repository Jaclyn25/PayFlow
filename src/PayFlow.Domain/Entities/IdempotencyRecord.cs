namespace PayFlow.Domain.Entities;

public class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public Guid? TransactionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
