using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.IntegrationTests;

public class ConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public ConcurrencyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task SequentialTransfers_WithInsufficientBalance_RejectsOverspend()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseSqlite(_connection)
            .Options;

        Guid senderId;

        using (var setupContext = new PayFlowDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();

            var sender = new User
            {
                FirstName = "Alice",
                LastName = "Smith",
                Email = "alice@payflow.local"
            };
            var senderWallet = new Wallet
            {
                UserId = sender.Id,
                Balance = 1000.00m,
                Currency = "EGP"
            };
            sender.Wallet = senderWallet;

            var receiver = new User
            {
                FirstName = "Bob",
                LastName = "Jones",
                Email = "bob@payflow.local"
            };
            var receiverWallet = new Wallet
            {
                UserId = receiver.Id,
                Balance = 500.00m,
                Currency = "EGP"
            };
            receiver.Wallet = receiverWallet;

            setupContext.Users.AddRange(sender, receiver);
            setupContext.Wallets.AddRange(senderWallet, receiverWallet);
            await setupContext.SaveChangesAsync();

            senderId = sender.Id;
        }

        var request1 = new TransferRequest("bob@payflow.local", 800.00m, "Transfer 1");
        var request2 = new TransferRequest("bob@payflow.local", 800.00m, "Transfer 2");

        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();

        // Act
        using var context1 = new PayFlowDbContext(options);
        var service1 = new TransferService(context1, new Mock<ICacheService>().Object);
        var result1 = await service1.TransferAsync(senderId, key1, request1);

        using var context2 = new PayFlowDbContext(options);
        var service2 = new TransferService(context2, new Mock<ICacheService>().Object);
        var act2 = () => service2.TransferAsync(senderId, key2, request2);

        // Assert
        result1.Should().NotBeNull();
        result1.Amount.Should().Be(800.00m);

        await act2.Should().ThrowAsync<InsufficientBalanceException>();

        using var verifyContext = new PayFlowDbContext(options);
        var finalSenderWallet = await verifyContext.Wallets.FirstAsync(w => w.UserId == senderId);
        finalSenderWallet.Balance.Should().Be(200.00m, "1000 - 800 = 200 EGP remaining.");
    }
}
