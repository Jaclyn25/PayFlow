using PayFlow.Domain.Enums;

namespace PayFlow.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public decimal ExchangeRate { get; set; } = 1.0m;
    public decimal? TargetAmount { get; set; }
    public string? TargetCurrency { get; set; }
    public Guid? SenderWalletId { get; set; }
    public Guid? ReceiverWalletId { get; set; }
    public string? Description { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public Guid? OriginalTransactionId { get; set; }
    public Guid? ReversalTransactionId { get; set; }
    public string? ReversalReason { get; set; }

    public Wallet? SenderWallet { get; set; }
    public Wallet? ReceiverWallet { get; set; }
    public Transaction? OriginalTransaction { get; set; }
    public Transaction? ReversalTransaction { get; set; }

    public static string GenerateReference()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var randomPart = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"TXN-{timestamp}-{randomPart}";
    }
}
