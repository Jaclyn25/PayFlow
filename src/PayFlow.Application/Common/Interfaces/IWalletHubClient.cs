using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IWalletHubClient
{
    Task BalanceUpdated(BalanceUpdatedNotification notification);
    Task TransferReceived(TransferReceivedNotification notification);
    Task TransferSent(TransferSentNotification notification);
}
