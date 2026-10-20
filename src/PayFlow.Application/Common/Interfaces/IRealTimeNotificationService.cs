using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IRealTimeNotificationService
{
    Task NotifyBalanceUpdatedAsync(Guid userId, BalanceUpdatedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyTransferReceivedAsync(Guid recipientUserId, TransferReceivedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyTransferSentAsync(Guid senderUserId, TransferSentNotification notification, CancellationToken cancellationToken = default);
}
