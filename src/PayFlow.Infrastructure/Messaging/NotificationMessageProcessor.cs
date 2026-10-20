using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Messaging;

public class NotificationMessageProcessor : INotificationMessageProcessor
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly IRealTimeNotificationService? _notificationService;
    private readonly ILogger<NotificationMessageProcessor> _logger;

    public NotificationMessageProcessor(
        IPayFlowDbContext dbContext,
        ILogger<NotificationMessageProcessor> logger,
        IRealTimeNotificationService? notificationService = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task ProcessAsync(string messagePayload, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(messagePayload);
        var root = doc.RootElement;

        var transactionIdStr = (root.TryGetProperty("TransactionId", out var idProp) ? idProp.GetString() : null) ?? "UNKNOWN";
        var reference = (root.TryGetProperty("Reference", out var refProp) ? refProp.GetString() : null) ?? "N/A";
        var amount = root.TryGetProperty("Amount", out var amtProp) ? amtProp.GetDecimal() : 0m;
        var feeAmount = root.TryGetProperty("FeeAmount", out var feeProp) ? feeProp.GetDecimal() : 0m;
        var currency = (root.TryGetProperty("Currency", out var curProp) ? curProp.GetString() : null) ?? "EGP";
        var targetAmount = root.TryGetProperty("TargetAmount", out var targetAmtProp) ? targetAmtProp.GetDecimal() : amount;
        var targetCurrency = (root.TryGetProperty("TargetCurrency", out var targetCurProp) ? targetCurProp.GetString() : null) ?? currency;

        Guid.TryParse(transactionIdStr, out var transactionId);

        Guid senderUserId = Guid.Empty;
        if (root.TryGetProperty("SenderUserId", out var senderProp) && senderProp.ValueKind == JsonValueKind.String)
        {
            Guid.TryParse(senderProp.GetString(), out senderUserId);
        }

        Guid receiverUserId = Guid.Empty;
        if (root.TryGetProperty("ReceiverUserId", out var receiverProp) && receiverProp.ValueKind == JsonValueKind.String)
        {
            Guid.TryParse(receiverProp.GetString(), out receiverUserId);
        }

        var occurredAt = root.TryGetProperty("OccurredAtUtc", out var occProp) && occProp.ValueKind == JsonValueKind.String && DateTime.TryParse(occProp.GetString(), out var occDt)
            ? occDt
            : DateTime.UtcNow;

        // 1. Durable Inbox Pattern Idempotency Check
        var alreadyProcessed = await _dbContext.InboxMessages
            .AnyAsync(i => i.MessageId == transactionIdStr, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation("[NOTIFICATION PROCESSOR] Duplicate event skipped for Transaction '{TransactionId}'.", transactionIdStr);
            return;
        }

        var inboxRecord = new InboxMessage
        {
            MessageId = transactionIdStr,
            EventType = "TransferCompleted",
            ProcessedAtUtc = DateTime.UtcNow
        };

        _dbContext.InboxMessages.Add(inboxRecord);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[NOTIFICATION PROCESSOR] Transfer notification recorded for transaction '{Reference}' (ID: {TransactionId}). Amount: {Amount:F2} {Currency}.",
            reference, transactionIdStr, amount, currency);

        // 2. Real-Time Push Notifications via SignalR Hub
        if (_notificationService != null)
        {
            try
            {
                // Notify Recipient
                if (receiverUserId != Guid.Empty)
                {
                    var receiverNotification = new TransferReceivedNotification(
                        transactionId,
                        reference,
                        senderUserId,
                        receiverUserId,
                        targetAmount > 0 ? targetAmount : amount,
                        targetCurrency,
                        occurredAt
                    );

                    await _notificationService.NotifyTransferReceivedAsync(receiverUserId, receiverNotification, cancellationToken);

                    var receiverWallet = await _dbContext.Wallets
                        .AsNoTracking()
                        .FirstOrDefaultAsync(w => w.UserId == receiverUserId, cancellationToken);

                    if (receiverWallet != null)
                    {
                        var balanceNotification = new BalanceUpdatedNotification(
                            receiverWallet.Id,
                            receiverUserId,
                            receiverWallet.Balance,
                            receiverWallet.Currency,
                            DateTime.UtcNow
                        );

                        await _notificationService.NotifyBalanceUpdatedAsync(receiverUserId, balanceNotification, cancellationToken);
                    }
                }

                // Notify Sender
                if (senderUserId != Guid.Empty)
                {
                    var senderNotification = new TransferSentNotification(
                        transactionId,
                        reference,
                        senderUserId,
                        receiverUserId,
                        amount,
                        feeAmount,
                        currency,
                        occurredAt
                    );

                    await _notificationService.NotifyTransferSentAsync(senderUserId, senderNotification, cancellationToken);

                    var senderWallet = await _dbContext.Wallets
                        .AsNoTracking()
                        .FirstOrDefaultAsync(w => w.UserId == senderUserId, cancellationToken);

                    if (senderWallet != null)
                    {
                        var balanceNotification = new BalanceUpdatedNotification(
                            senderWallet.Id,
                            senderUserId,
                            senderWallet.Balance,
                            senderWallet.Currency,
                            DateTime.UtcNow
                        );

                        await _notificationService.NotifyBalanceUpdatedAsync(senderUserId, balanceNotification, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[NOTIFICATION PROCESSOR] Failed to dispatch real-time push notification for transaction '{TransactionId}'.", transactionIdStr);
            }
        }
    }
}
