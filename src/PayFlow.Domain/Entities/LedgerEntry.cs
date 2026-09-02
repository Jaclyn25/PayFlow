using PayFlow.Domain.Enums;

namespace PayFlow.Domain.Entities;

public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public Guid WalletId { get; set; }
    public LedgerEntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Transaction? Transaction { get; set; }
    public Wallet? Wallet { get; set; }
}
