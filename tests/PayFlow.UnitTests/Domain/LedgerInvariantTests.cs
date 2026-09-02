using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Domain;

public class LedgerInvariantTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly WalletService _walletService;
    private readonly TransferService _transferService;

    private readonly User _alice;
    private readonly User _bob;

    public LedgerInvariantTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

        // Ensure System Wallet
        var systemUser = new User
        {
            Id = SystemConstants.SystemUserId,
            FirstName = "System",
            LastName = "Treasury",
            Email = "system.treasury@payflow.local"
        };
        var systemWallet = new Wallet
        {
            Id = SystemConstants.SystemWalletId,
            UserId = systemUser.Id,
            Balance = 0m
        };

        _alice = new User { FirstName = "Alice", LastName = "Smith", Email = "alice@example.com" };
        _alice.Wallet = new Wallet { UserId = _alice.Id, Balance = 1000m };

        _bob = new User { FirstName = "Bob", LastName = "Jones", Email = "bob@example.com" };
        _bob.Wallet = new Wallet { UserId = _bob.Id, Balance = 500m };

        _dbContext.Users.AddRange(systemUser, _alice, _bob);
        _dbContext.Wallets.AddRange(systemWallet, _alice.Wallet, _bob.Wallet);
        _dbContext.SaveChanges();

        _walletService = new WalletService(_dbContext, _cacheMock.Object);
        _transferService = new TransferService(_dbContext, _cacheMock.Object);
    }

    [Fact]
    public async Task AllTransactionTypes_MustProduceBalancedDoubleEntryLedgerEntries()
    {
        // Act: Perform Deposit, Withdrawal, and Transfer
        var depositTxn = await _walletService.DepositAsync(_alice.Id, new DepositRequest(200m, "Deposit"));
        var withdrawTxn = await _walletService.WithdrawAsync(_alice.Id, new WithdrawRequest(100m, "Withdraw"));
        var transferTxn = await _transferService.TransferAsync(_alice.Id, Guid.NewGuid().ToString(), new TransferRequest("bob@example.com", 300m, "Transfer"));

        // Assert: For each transaction, SUM(Debit Amount) == SUM(Credit Amount)
        var transactions = new[] { depositTxn.Id, withdrawTxn.Id, transferTxn.Id };

        foreach (var txnId in transactions)
        {
            var entries = await _dbContext.LedgerEntries
                .Where(l => l.TransactionId == txnId)
                .ToListAsync();

            entries.Should().HaveCount(2, $"Transaction {txnId} must have exactly 2 double-entry ledger rows (1 Debit, 1 Credit).");

            var totalDebit = entries.Where(l => l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
            var totalCredit = entries.Where(l => l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);

            totalDebit.Should().Be(totalCredit, $"Transaction {txnId} must be accounting-balanced (Total Debit == Total Credit).");
        }
    }
}
