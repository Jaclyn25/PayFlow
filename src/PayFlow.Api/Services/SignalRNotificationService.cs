using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PayFlow.Api.Hubs;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;

namespace PayFlow.Api.Services;

public class SignalRNotificationService : IRealTimeNotificationService
{
    private readonly IHubContext<WalletHub, IWalletHubClient> _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<WalletHub, IWalletHubClient> hubContext,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyBalanceUpdatedAsync(Guid userId, BalanceUpdatedNotification notification, CancellationToken cancellationToken = default)
    {
        var userGroup = WalletHub.GetUserGroupName(userId);
        _logger.LogInformation("[SIGNALR] Broadcasting BalanceUpdated to user group '{Group}'. New Balance: {Balance} {Currency}",
            userGroup, notification.NewBalance, notification.Currency);

        // Targeted delivery to user group and user identifier to prevent cross-tenant leakage
        await _hubContext.Clients.Group(userGroup).BalanceUpdated(notification);
        await _hubContext.Clients.User(userId.ToString()).BalanceUpdated(notification);
    }

    public async Task NotifyTransferReceivedAsync(Guid recipientUserId, TransferReceivedNotification notification, CancellationToken cancellationToken = default)
    {
        var userGroup = WalletHub.GetUserGroupName(recipientUserId);
        _logger.LogInformation("[SIGNALR] Broadcasting TransferReceived to user group '{Group}'. Reference: {Reference}, Amount: {Amount} {Currency}",
            userGroup, notification.Reference, notification.Amount, notification.Currency);

        await _hubContext.Clients.Group(userGroup).TransferReceived(notification);
        await _hubContext.Clients.User(recipientUserId.ToString()).TransferReceived(notification);
    }

    public async Task NotifyTransferSentAsync(Guid senderUserId, TransferSentNotification notification, CancellationToken cancellationToken = default)
    {
        var userGroup = WalletHub.GetUserGroupName(senderUserId);
        _logger.LogInformation("[SIGNALR] Broadcasting TransferSent to user group '{Group}'. Reference: {Reference}, Amount: {Amount} {Currency}",
            userGroup, notification.Reference, notification.Amount, notification.Currency);

        await _hubContext.Clients.Group(userGroup).TransferSent(notification);
        await _hubContext.Clients.User(senderUserId.ToString()).TransferSent(notification);
    }
}
