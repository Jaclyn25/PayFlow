namespace PayFlow.Application.DTOs;

public record BalanceUpdatedNotification(
    Guid WalletId,
    Guid UserId,
    decimal NewBalance,
    string Currency,
    DateTime UpdatedAtUtc
);

public record TransferReceivedNotification(
    Guid TransactionId,
    string Reference,
    Guid SenderUserId,
    Guid ReceiverUserId,
    decimal Amount,
    string Currency,
    DateTime OccurredAtUtc
);

public record TransferSentNotification(
    Guid TransactionId,
    string Reference,
    Guid SenderUserId,
    Guid ReceiverUserId,
    decimal Amount,
    decimal FeeAmount,
    string Currency,
    DateTime OccurredAtUtc
);
