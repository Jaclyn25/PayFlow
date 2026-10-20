using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Messaging;

public class NotificationMessageProcessor : INotificationMessageProcessor
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly ILogger<NotificationMessageProcessor> _logger;

    public NotificationMessageProcessor(
        IPayFlowDbContext dbContext,
        ILogger<NotificationMessageProcessor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ProcessAsync(string messagePayload, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(messagePayload);
        var root = doc.RootElement;

        var transactionId = root.TryGetProperty("TransactionId", out var idProp) ? idProp.GetString() : "UNKNOWN";
        var reference = root.TryGetProperty("Reference", out var refProp) ? refProp.GetString() : "N/A";
        var amount = root.TryGetProperty("Amount", out var amtProp) ? amtProp.GetDecimal() : 0m;
        var currency = root.TryGetProperty("Currency", out var curProp) ? curProp.GetString() : "EGP";

        // Durable Inbox Pattern Idempotency Check
        var alreadyProcessed = await _dbContext.InboxMessages
            .AnyAsync(i => i.MessageId == transactionId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation("[NOTIFICATION PROCESSOR] Duplicate event skipped for Transaction '{TransactionId}'.", transactionId);
            return;
        }

        var inboxRecord = new InboxMessage
        {
            MessageId = transactionId!,
            EventType = "TransferCompleted",
            ProcessedAtUtc = DateTime.UtcNow
        };

        _dbContext.InboxMessages.Add(inboxRecord);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[NOTIFICATION PROCESSOR] Transfer notification processed for transaction '{Reference}' (ID: {TransactionId}). Amount: {Amount:F2} {Currency}.",
            reference, transactionId, amount, currency);
    }
}
