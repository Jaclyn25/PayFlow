using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class TransactionServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly TransactionService _transactionService;

    private readonly User _userA;
    private readonly User _userB;
    private readonly User _userC;
    private readonly Transaction _txnAB;

    public TransactionServiceTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PayFlowDbContext(options);

        _userA = new User { FirstName = "UserA", LastName = "A", Email = "usera@payflow.local" };
        _userA.Wallet = new Wallet { UserId = _userA.Id, Balance = 1000m };

        _userB = new User { FirstName = "UserB", LastName = "B", Email = "userb@payflow.local" };
        _userB.Wallet = new Wallet { UserId = _userB.Id, Balance = 500m };

        _userC = new User { FirstName = "UserC", LastName = "C", Email = "userc@payflow.local" };
        _userC.Wallet = new Wallet { UserId = _userC.Id, Balance = 100m };

        _txnAB = new Transaction
        {
            Reference = "TXN-0001",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 100m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet.Id,
            ReceiverWalletId = _userB.Wallet.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Users.AddRange(_userA, _userB, _userC);
        _dbContext.Wallets.AddRange(_userA.Wallet, _userB.Wallet, _userC.Wallet);
        _dbContext.Transactions.Add(_txnAB);
        _dbContext.SaveChanges();

        _transactionService = new TransactionService(_dbContext);
    }

    [Fact]
    public async Task GetTransactionById_AuthorizedSender_ReturnsTransaction()
    {
        // Act
        var result = await _transactionService.GetTransactionByIdAsync(_userA.Id, _txnAB.Id);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(_txnAB.Id);
    }

    [Fact]
    public async Task GetTransactionById_AuthorizedReceiver_ReturnsTransaction()
    {
        // Act
        var result = await _transactionService.GetTransactionByIdAsync(_userB.Id, _txnAB.Id);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(_txnAB.Id);
    }

    [Fact]
    public async Task GetTransactionById_UnrelatedUser_ThrowsUnauthorizedDomainException()
    {
        // Act: User C attempts to view User A -> User B transaction
        var act = () => _transactionService.GetTransactionByIdAsync(_userC.Id, _txnAB.Id);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedDomainException>()
            .WithMessage("*permission to view this transaction*");
    }
}
