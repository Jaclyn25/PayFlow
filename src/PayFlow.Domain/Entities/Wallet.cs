using PayFlow.Domain.Exceptions;

namespace PayFlow.Domain.Entities;

public class Wallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public decimal Balance { get; set; } = 0.00m;
    public string Currency { get; set; } = "EGP";
    
    // PostgreSQL xmin concurrency token or concurrency version
    public uint Version { get; set; }
    
    public bool IsFrozen { get; set; } = false;
    public string? FreezeReason { get; set; }
    public DateTime? FrozenAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }

    public void Freeze(string? reason = null)
    {
        IsFrozen = true;
        FreezeReason = reason;
        FrozenAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Unfreeze()
    {
        IsFrozen = false;
        FreezeReason = null;
        FrozenAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deposit(decimal amount)
    {
        if (IsFrozen)
        {
            throw new InvalidTransactionException("Cannot deposit to a frozen wallet.");
        }

        if (amount <= 0)
        {
            throw new InvalidTransactionException("Deposit amount must be greater than zero.");
        }

        Balance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Withdraw(decimal amount)
    {
        if (IsFrozen)
        {
            throw new InvalidTransactionException("Cannot withdraw from a frozen wallet.");
        }

        if (amount <= 0)
        {
            throw new InvalidTransactionException("Withdrawal amount must be greater than zero.");
        }

        if (Balance < amount)
        {
            throw new InsufficientBalanceException(Balance, amount);
        }

        Balance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
