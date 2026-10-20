using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using PayFlow.Domain.Entities;
using PayFlow.Infrastructure.Messaging;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Messaging;

public class NotificationMessageProcessorTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ILogger<NotificationMessageProcessor>> _loggerMock = new();
    private readonly NotificationMessageProcessor _processor;

    public NotificationMessageProcessorTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);
        _processor = new NotificationMessageProcessor(_dbContext, _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessAsync_NewMessage_CreatesInboxMessage()
    {
        // Arrange
        var txnId = Guid.NewGuid().ToString();
        var payload = JsonSerializer.Serialize(new
        {
            TransactionId = txnId,
            Reference = "TXN-TEST-100",
            Amount = 150.00m,
            Currency = "EGP"
        });

        // Act
        await _processor.ProcessAsync(payload);

        // Assert
        var inboxItem = await _dbContext.InboxMessages.FirstOrDefaultAsync(i => i.MessageId == txnId);
        inboxItem.Should().NotBeNull();
        inboxItem!.EventType.Should().Be("TransferCompleted");
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
    }
}
