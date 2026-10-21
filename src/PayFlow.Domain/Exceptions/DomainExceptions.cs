namespace PayFlow.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class InsufficientBalanceException : DomainException
{
    public InsufficientBalanceException(decimal balance, decimal requestedAmount)
        : base($"Insufficient wallet balance. Current balance: {balance:F2}, requested: {requestedAmount:F2}.")
    {
    }
}

public class DuplicateIdempotencyException : DomainException
{
    public string Key { get; }
    public DuplicateIdempotencyException(string key)
        : base($"A transaction with idempotency key '{key}' has already been processed.")
    {
        Key = key;
    }
}

public class WalletNotFoundException : DomainException
{
    public WalletNotFoundException(Guid walletId)
        : base($"Wallet with ID '{walletId}' was not found.")
    {
    }

    public WalletNotFoundException(string identifier)
        : base($"Wallet for '{identifier}' was not found.")
    {
    }
}

public class UserNotFoundException : DomainException
{
    public UserNotFoundException(string identifier)
        : base($"User '{identifier}' was not found.")
    {
    }

    public UserNotFoundException(Guid userId)
        : base($"User with ID '{userId}' was not found.")
    {
    }
}

public class InvalidTransactionException : DomainException
{
    public InvalidTransactionException(string message) : base(message) { }
}

public class UnauthorizedDomainException : DomainException
{
    public UnauthorizedDomainException(string message) : base(message) { }
}

public class WebhookSubscriptionNotFoundException : DomainException
{
    public WebhookSubscriptionNotFoundException(Guid subscriptionId)
        : base($"Webhook subscription with ID '{subscriptionId}' was not found.")
    {
    }

    public WebhookSubscriptionNotFoundException(string identifier)
        : base($"Webhook subscription '{identifier}' was not found.")
    {
    }
}

public class WebhookDeliveryNotFoundException : DomainException
{
    public WebhookDeliveryNotFoundException(Guid deliveryId)
        : base($"Webhook delivery log with ID '{deliveryId}' was not found.")
    {
    }
}
