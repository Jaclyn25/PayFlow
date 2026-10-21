namespace PayFlow.Domain.Constants;

public static class WebhookEventConstants
{
    public const string TransferCompleted = "transfer.completed";
    public const string TransferFailed = "transfer.failed";
    public const string DepositSucceeded = "deposit.succeeded";
    public const string WithdrawalCompleted = "withdrawal.completed";
    public const string Ping = "ping";

    public static readonly IReadOnlyList<string> AllSupportedEvents = new[]
    {
        TransferCompleted,
        TransferFailed,
        DepositSucceeded,
        WithdrawalCompleted,
        Ping
    };

    public static bool IsSupported(string eventType) =>
        AllSupportedEvents.Contains(eventType, StringComparer.OrdinalIgnoreCase);
}
