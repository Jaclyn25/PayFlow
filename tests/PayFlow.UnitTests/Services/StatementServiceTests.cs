using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Reporting;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class StatementServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly StatementReportGenerator _reportGenerator;
    private readonly StatementService _statementService;

    private readonly User _userA;
    private readonly User _userB;

    public StatementServiceTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);
        _reportGenerator = new StatementReportGenerator();
        _statementService = new StatementService(_dbContext, _reportGenerator);

        _userA = new User { FirstName = "John", LastName = "Doe", Email = "john.doe@payflow.local" };
        _userA.Wallet = new Wallet { UserId = _userA.Id, Balance = 550m, Currency = "EGP" };

        _userB = new User { FirstName = "Jane", LastName = "Roe", Email = "jane.roe@payflow.local" };
        _userB.Wallet = new Wallet { UserId = _userB.Id, Balance = 1000m, Currency = "EGP" };

        _dbContext.Users.AddRange(_userA, _userB);
        _dbContext.Wallets.AddRange(_userA.Wallet, _userB.Wallet);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task GetStatementDataAsync_ReconciliationInvariant_OpeningPlusCreditsMinusDebitsEqualsClosing()
    {
        // Arrange
        var baseDate = DateTime.UtcNow;

        // Entry 1 (before window): Deposit of 500 -> Opening balance will be 500
        var oldTxn = new Transaction { Reference = "TXN-OLD-01", Type = TransactionType.Deposit, Amount = 500m, Currency = "EGP" };
        var oldEntry = new LedgerEntry
        {
            TransactionId = oldTxn.Id,
            WalletId = _userA.Wallet!.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 500m,
            BalanceAfter = 500m,
            CreatedAtUtc = baseDate.AddDays(-20)
        };

        // Window entries: [baseDate - 15 days, baseDate]
        var txn1 = new Transaction { Reference = "TXN-001", Type = TransactionType.Transfer, Amount = 100m, Currency = "EGP" };
        var entry1 = new LedgerEntry
        {
            TransactionId = txn1.Id,
            WalletId = _userA.Wallet.Id,
            EntryType = LedgerEntryType.Debit,
            Amount = 100m,
            BalanceAfter = 400m,
            CreatedAtUtc = baseDate.AddDays(-10)
        };

        var txn2 = new Transaction { Reference = "TXN-002", Type = TransactionType.Transfer, Amount = 50m, Currency = "EGP" };
        var entry2 = new LedgerEntry
        {
            TransactionId = txn2.Id,
            WalletId = _userA.Wallet.Id,
            EntryType = LedgerEntryType.Debit,
            Amount = 50m,
            BalanceAfter = 350m,
            CreatedAtUtc = baseDate.AddDays(-5)
        };

        var txn3 = new Transaction { Reference = "TXN-003", Type = TransactionType.Deposit, Amount = 200m, Currency = "EGP" };
        var entry3 = new LedgerEntry
        {
            TransactionId = txn3.Id,
            WalletId = _userA.Wallet.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 200m,
            BalanceAfter = 550m,
            CreatedAtUtc = baseDate.AddDays(-2)
        };

        _dbContext.Transactions.AddRange(oldTxn, txn1, txn2, txn3);
        _dbContext.LedgerEntries.AddRange(oldEntry, entry1, entry2, entry3);
        await _dbContext.SaveChangesAsync();

        var fromDate = baseDate.AddDays(-15);
        var toDate = baseDate;

        // Act
        var statement = await _statementService.GetStatementDataAsync(_userA.Id, _userA.Wallet.Id, fromDate, toDate);

        // Assert - Reconciliation Summary Invariant
        statement.Should().NotBeNull();
        statement.OpeningBalance.Should().Be(500m);
        statement.TotalCredits.Should().Be(200m);
        statement.TotalDebits.Should().Be(150m);
        statement.NetMovement.Should().Be(50m);
        statement.ClosingBalance.Should().Be(550m);

        // Invariant: Opening + Credits - Debits == Closing
        (statement.OpeningBalance + statement.TotalCredits - statement.TotalDebits).Should().Be(statement.ClosingBalance);

        // Assert - Entries
        statement.Entries.Should().HaveCount(3);
        statement.VerificationHash.Should().NotBeNullOrWhiteSpace().And.HaveLength(64);
    }

    [Fact]
    public async Task GetStatementDataAsync_DateFiltering_CorrectlyExcludesOutRangeEntries()
    {
        // Arrange: 4 entries at different timestamps
        var baseDate = DateTime.UtcNow;

        var txnBefore = new Transaction { Reference = "TXN-PRE", Amount = 1000m };
        var entryBefore = new LedgerEntry
        {
            TransactionId = txnBefore.Id,
            WalletId = _userA.Wallet!.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 1000m,
            BalanceAfter = 1000m,
            CreatedAtUtc = baseDate.AddDays(-40)
        };

        var txnWithdraw = new Transaction { Reference = "TXN-WTH", Amount = 200m };
        var entryWithdraw = new LedgerEntry
        {
            TransactionId = txnWithdraw.Id,
            WalletId = _userA.Wallet.Id,
            EntryType = LedgerEntryType.Debit,
            Amount = 200m,
            BalanceAfter = 800m,
            CreatedAtUtc = baseDate.AddDays(-35)
        };

        var txnInside = new Transaction { Reference = "TXN-IN", Amount = 150m };
        var entryInside = new LedgerEntry
        {
            TransactionId = txnInside.Id,
            WalletId = _userA.Wallet.Id,
            EntryType = LedgerEntryType.Debit,
            Amount = 150m,
            BalanceAfter = 650m,
            CreatedAtUtc = baseDate.AddDays(-20)
        };

        var txnAfter = new Transaction { Reference = "TXN-POST", Amount = 300m };
        var entryAfter = new LedgerEntry
        {
            TransactionId = txnAfter.Id,
            WalletId = _userA.Wallet.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 300m,
            BalanceAfter = 950m,
            CreatedAtUtc = baseDate.AddDays(-5)
        };

        _dbContext.Transactions.AddRange(txnBefore, txnWithdraw, txnInside, txnAfter);
        _dbContext.LedgerEntries.AddRange(entryBefore, entryWithdraw, entryInside, entryAfter);
        await _dbContext.SaveChangesAsync();

        // Window: [-30 days, -10 days]
        var fromDate = baseDate.AddDays(-30);
        var toDate = baseDate.AddDays(-10);

        // Act
        var statement = await _statementService.GetStatementDataAsync(_userA.Id, _userA.Wallet.Id, fromDate, toDate);

        // Assert
        statement.OpeningBalance.Should().Be(800m); // Last balance before window (1000 - 200 = 800)
        statement.Entries.Should().HaveCount(1);
        statement.Entries[0].Reference.Should().Be("TXN-IN");
        statement.TotalDebits.Should().Be(150m);
        statement.TotalCredits.Should().Be(0m);
        statement.ClosingBalance.Should().Be(650m); // 800 - 150 = 650
    }

    [Fact]
    public async Task GetStatementDataAsync_UnauthorizedUser_ThrowsUnauthorizedDomainException()
    {
        // Act: User B attempts to access User A's wallet statement
        var act = () => _statementService.GetStatementDataAsync(_userB.Id, _userA.Wallet!.Id, null, null);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedDomainException>()
            .WithMessage("*permission to view statements*");
    }

    [Fact]
    public async Task GenerateCsvStatementAsync_ProducesValidRfc4180Content()
    {
        // Arrange
        var txn = new Transaction { Reference = "TXN-CSV-01", Description = "Test Transfer, with comma", Amount = 50m };
        var entry = new LedgerEntry
        {
            TransactionId = txn.Id,
            WalletId = _userA.Wallet!.Id,
            EntryType = LedgerEntryType.Debit,
            Amount = 50m,
            BalanceAfter = 500m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        _dbContext.Transactions.Add(txn);
        _dbContext.LedgerEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        // Act
        var csvBytes = await _statementService.GenerateCsvStatementAsync(_userA.Id, _userA.Wallet.Id, null, null);
        var csvContent = Encoding.UTF8.GetString(csvBytes);

        // Assert RFC 4180 Format
        csvContent.Should().Contain("PayFlow Official Account Statement");
        csvContent.Should().Contain("Reconciliation Summary");
        csvContent.Should().Contain("Date (UTC),Reference,Type,Description,Debit,Credit,Balance After,Currency");
        csvContent.Should().Contain("\"Test Transfer, with comma\""); // Comma escaped in quotes
        csvContent.Should().Contain("TXN-CSV-01");
        csvContent.Should().Contain("Verification Hash");
    }

    [Fact]
    public async Task GeneratePdfStatementAsync_ProducesValidPdfDocument()
    {
        // Arrange
        var txn = new Transaction { Reference = "TXN-PDF-01", Description = "Deposit Funds", Amount = 100m };
        var entry = new LedgerEntry
        {
            TransactionId = txn.Id,
            WalletId = _userA.Wallet!.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 100m,
            BalanceAfter = 650m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        _dbContext.Transactions.Add(txn);
        _dbContext.LedgerEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        // Act
        var pdfBytes = await _statementService.GeneratePdfStatementAsync(_userA.Id, _userA.Wallet.Id, null, null);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty();
        var header = Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        header.Should().Be("%PDF-"); // Standard PDF magic bytes
        pdfBytes.Length.Should().BeGreaterThan(1000);
    }
}
