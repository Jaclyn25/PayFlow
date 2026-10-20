using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;
using PayFlow.Infrastructure.Messaging;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Messaging;

public class NotificationMessageProcessorTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ILogger<NotificationMessageProcessor>> _loggerMock = new();
    private readonly Mock<IRealTimeNotificationService> _notificationServiceMock = new();
    private readonly NotificationMessageProcessor _processor;

    public NotificationMessageProcessorTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);
        _processor = new NotificationMessageProcessor(_dbContext, _loggerMock.Object, _notificationServiceMock.Object);
    }

    [Fact]
    public async Task ProcessAsync_NewMessage_CreatesInboxMessageAndBroadcastsPushNotifications()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var senderWallet = new Wallet { Id = Guid.NewGuid(), UserId = senderId, Balance = 800m, Currency = "EGP" };
        var receiverWallet = new Wallet { Id = Guid.NewGuid(), UserId = receiverId, Balance = 450m, Currency = "EGP" };
        _dbContext.Wallets.AddRange(senderWallet, receiverWallet);
        await _dbContext.SaveChangesAsync();

        var txnId = Guid.NewGuid().ToString();
        var payload = JsonSerializer.Serialize(new
        {
            TransactionId = txnId,
            Reference = "TXN-TEST-100",
            SenderUserId = senderId.ToString(),
            ReceiverUserId = receiverId.ToString(),
            Amount = 150.00m,
            FeeAmount = 5.00m,
            Currency = "EGP"
        });

        // Act
        await _processor.ProcessAsync(payload);

        // Assert
        var inboxItem = await _dbContext.InboxMessages.FirstOrDefaultAsync(i => i.MessageId == txnId);
        inboxItem.Should().NotBeNull();
        inboxItem!.EventType.Should().Be("TransferCompleted");

        // Verify recipient notifications
        _notificationServiceMock.Verify(n => n.NotifyTransferReceivedAsync(
            receiverId,
            It.Is<TransferReceivedNotification>(r => r.Reference == "TXN-TEST-100" && r.Amount == 150.00m && r.ReceiverUserId == receiverId),
            It.IsAny<CancellationToken>()), Times.Once);

        _notificationServiceMock.Verify(n => n.NotifyBalanceUpdatedAsync(
            receiverId,
            It.Is<BalanceUpdatedNotification>(b => b.UserId == receiverId && b.NewBalance == 450m),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify sender notifications
        _notificationServiceMock.Verify(n => n.NotifyTransferSentAsync(
            senderId,
            It.Is<TransferSentNotification>(s => s.Reference == "TXN-TEST-100" && s.Amount == 150.00m && s.FeeAmount == 5.00m),
            It.IsAny<CancellationToken>()), Times.Once);

        _notificationServiceMock.Verify(n => n.NotifyBalanceUpdatedAsync(
            senderId,
            It.Is<BalanceUpdatedNotification>(b => b.UserId == senderId && b.NewBalance == 800m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateMessage_SkipsProcessingIdempotently()
    {
        // Arrange
        var txnId = Guid.NewGuid().ToString();
        var payload = JsonSerializer.Serialize(new
        {
            TransactionId = txnId,
            Reference = "TXN-TEST-200",
            Amount = 250.00m,
            Currency = "USD"
        });

        // Pre-insert inbox message
        _dbContext.InboxMessages.Add(new InboxMessage
        {
            MessageId = txnId,
            EventType = "TransferCompleted",
            ProcessedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        await _processor.ProcessAsync(payload);

        // Assert
        var count = await _dbContext.InboxMessages.CountAsync(i => i.MessageId == txnId);
        count.Should().Be(1);

        // Verify no push notifications sent on duplicate
        _notificationServiceMock.Verify(n => n.NotifyTransferReceivedAsync(It.IsAny<Guid>(), It.IsAny<TransferReceivedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationServiceMock.Verify(n => n.NotifyBalanceUpdatedAsync(It.IsAny<Guid>(), It.IsAny<BalanceUpdatedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
