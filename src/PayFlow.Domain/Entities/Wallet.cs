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
    
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }

    public void Deposit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new InvalidTransactionException("Deposit amount must be greater than zero.");
        }

        Balance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Withdraw(decimal amount)
    {
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
