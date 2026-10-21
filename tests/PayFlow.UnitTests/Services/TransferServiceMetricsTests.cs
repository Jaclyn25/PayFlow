using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;

namespace PayFlow.UnitTests.Services;

public class TransferServiceMetricsTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<IPayFlowMetrics> _metricsMock = new();
    private readonly TransferService _transferService;

    private readonly User _sender;
    private readonly User _receiver;

    public TransferServiceMetricsTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

        _sender = new User
        {
            FirstName = "Alice",
            LastName = "Sender",
            Email = "alice.sender@metrics.local"
        };
        _sender.Wallet = new Wallet
        {
            UserId = _sender.Id,
            Balance = 500.00m,
            Currency = "EGP"
        };

        _receiver = new User
        {
            FirstName = "Bob",
            LastName = "Receiver",
            Email = "bob.receiver@metrics.local"
        };
        _receiver.Wallet = new Wallet
        {
            UserId = _receiver.Id,
            Balance = 100.00m,
            Currency = "EGP"
        };

        _dbContext.Users.AddRange(_sender, _receiver);
        _dbContext.Wallets.AddRange(_sender.Wallet, _receiver.Wallet);
        _dbContext.SaveChanges();

        _transferService = new TransferService(
            _dbContext,
            _cacheServiceMock.Object,
            metrics: _metricsMock.Object);
    }

    [Fact]
    public async Task TransferAsync_WhenCompleted_RecordsCompletedMetric()
    {
        // Arrange
        var request = new TransferRequest(_receiver.Email, 150m, "Payment");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var response = await _transferService.TransferAsync(_sender.Id, idempotencyKey, request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Completed");

        _metricsMock.Verify(m => m.RecordTransfer(
            "Completed",
            "EGP",
            It.Is<double>(duration => duration >= 0)), Times.Once);
    }

    [Fact]
    public async Task TransferAsync_WhenFailsWithInsufficientBalance_RecordsFailedMetric()
    {
        // Arrange - request more than 500 balance
        var request = new TransferRequest(_receiver.Email, 999m, "Overdraft attempt");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = async () => await _transferService.TransferAsync(_sender.Id, idempotencyKey, request);

        // Assert
        await act.Should().ThrowAsync<InsufficientBalanceException>();

        _metricsMock.Verify(m => m.RecordTransfer(
            "Failed",
            "EGP",
            It.Is<double>(duration => duration >= 0)), Times.Once);
    }
}
