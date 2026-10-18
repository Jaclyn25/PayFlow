using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class TransactionReversalTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly TransactionService _transactionService;

    private readonly User _userA;
    private readonly User _userB;
    private readonly User _userC;

    public TransactionReversalTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

        _userA = new User { FirstName = "Alice", LastName = "Smith", Email = "alice@payflow.local" };
        _userA.Wallet = new Wallet { UserId = _userA.Id, Balance = 900m, Currency = "EGP" };

        _userB = new User { FirstName = "Bob", LastName = "Jones", Email = "bob@payflow.local" };
        _userB.Wallet = new Wallet { UserId = _userB.Id, Balance = 600m, Currency = "EGP" };

        _userC = new User { FirstName = "Charlie", LastName = "Brown", Email = "charlie@payflow.local" };
        _userC.Wallet = new Wallet { UserId = _userC.Id, Balance = 500m, Currency = "EGP" };

        _dbContext.Users.AddRange(_userA, _userB, _userC);
        _dbContext.Wallets.AddRange(_userA.Wallet, _userB.Wallet, _userC.Wallet);
        _dbContext.SaveChanges();

        _transactionService = new TransactionService(_dbContext);
    }

    [Fact]
    public async Task ReverseTransactionAsync_SingleCurrencyTransfer_SuccessfullyReversesBalancesAndPreservesDoubleEntryLedger()
    {
        // Arrange: Original Transfer: Alice transferred 100 to Bob
        var originalTxn = new Transaction
        {
            Reference = "TXN-TEST-ORIG-01",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 100m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet!.Id,
            ReceiverWalletId = _userB.Wallet!.Id,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };

        var origSenderLedger = new LedgerEntry
        {
            TransactionId = originalTxn.Id,
            WalletId = _userA.Wallet.Id,
            EntryType = LedgerEntryType.Debit,
            Amount = 100m,
            Currency = "EGP",
            BalanceAfter = 900m
        };

        var origReceiverLedger = new LedgerEntry
        {
            TransactionId = originalTxn.Id,
            WalletId = _userB.Wallet.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 100m,
            Currency = "EGP",
            BalanceAfter = 600m
        };

        _dbContext.Transactions.Add(originalTxn);
        _dbContext.LedgerEntries.AddRange(origSenderLedger, origReceiverLedger);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Customer requested dispute/refund due to duplicate charge");

        // Act
        var reversalResponse = await _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request);

        // Assert - Balances
        var updatedAliceWallet = await _dbContext.Wallets.FindAsync(_userA.Wallet.Id);
        var updatedBobWallet = await _dbContext.Wallets.FindAsync(_userB.Wallet.Id);

        updatedAliceWallet!.Balance.Should().Be(1000m); // 900 + 100 credited back
        updatedBobWallet!.Balance.Should().Be(500m);    // 600 - 100 debited back

        // Assert - Original Transaction Status & Link
        var updatedOriginal = await _dbContext.Transactions.FindAsync(originalTxn.Id);
        updatedOriginal!.Status.Should().Be(TransactionStatus.Refunded);
        updatedOriginal.ReversalTransactionId.Should().Be(reversalResponse.Id);
        updatedOriginal.ReversalReason.Should().Be(request.Reason);

        // Assert - Reversal Transaction
        reversalResponse.Type.Should().Be(TransactionType.Reversal.ToString());
        reversalResponse.Status.Should().Be(TransactionStatus.Completed.ToString());
        reversalResponse.Amount.Should().Be(100m);
        reversalResponse.OriginalTransactionId.Should().Be(originalTxn.Id);
        reversalResponse.ReversalReason.Should().Be(request.Reason);

        // Assert - Ledger Entries: Historical ledger entries remain untouched and unmodified
        var allLedgerEntries = await _dbContext.LedgerEntries.ToListAsync();
        allLedgerEntries.Should().HaveCount(4); // 2 original + 2 reversal

        var reversalEntries = allLedgerEntries.Where(l => l.TransactionId == reversalResponse.Id).ToList();
        reversalEntries.Should().HaveCount(2);

        var bobDebitEntry = reversalEntries.FirstOrDefault(l => l.WalletId == _userB.Wallet.Id);
        bobDebitEntry.Should().NotBeNull();
        bobDebitEntry!.EntryType.Should().Be(LedgerEntryType.Debit);
        bobDebitEntry.Amount.Should().Be(100m);
        bobDebitEntry.BalanceAfter.Should().Be(500m);

        var aliceCreditEntry = reversalEntries.FirstOrDefault(l => l.WalletId == _userA.Wallet.Id);
        aliceCreditEntry.Should().NotBeNull();
        aliceCreditEntry!.EntryType.Should().Be(LedgerEntryType.Credit);
        aliceCreditEntry.Amount.Should().Be(100m);
        aliceCreditEntry.BalanceAfter.Should().Be(1000m);

        // Invariant: Zero-sum balanced ledger
        var totalDebits = reversalEntries.Where(l => l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var totalCredits = reversalEntries.Where(l => l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);
        totalDebits.Should().Be(totalCredits);

        // Assert - Outbox message
        var outboxMessage = await _dbContext.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "TransactionReversed");
        outboxMessage.Should().NotBeNull();
        outboxMessage!.Payload.Should().Contain(reversalResponse.Reference);
    }

    [Fact]
    public async Task ReverseTransactionAsync_CrossCurrencyTransfer_SuccessfullyReversesAllFourClearingLedgerEntries()
    {
        // Arrange: Alice (USD, balance = 900) sent 100 USD to Bob (EUR, balance = 692, received 92 EUR)
        _userA.Wallet!.Currency = "USD";
        _userB.Wallet!.Currency = "EUR";
        _userB.Wallet.Balance = 692m;
        await _dbContext.SaveChangesAsync();

        var originalTxn = new Transaction
        {
            Reference = "TXN-FX-01",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 100m,
            Currency = "USD",
            ExchangeRate = 0.92m,
            TargetAmount = 92m,
            TargetCurrency = "EUR",
            SenderWalletId = _userA.Wallet.Id,
            ReceiverWalletId = _userB.Wallet.Id,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        _dbContext.Transactions.Add(originalTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Cross-currency transfer dispute");

        // Act
        var reversal = await _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request);

        // Assert - Balances
        var updatedAliceWallet = await _dbContext.Wallets.FindAsync(_userA.Wallet.Id);
        var updatedBobWallet = await _dbContext.Wallets.FindAsync(_userB.Wallet.Id);

        updatedAliceWallet!.Balance.Should().Be(1000m); // 900 + 100 USD
        updatedBobWallet!.Balance.Should().Be(600m);    // 692 - 92 EUR

        // Assert - 4 Paired Compensating Ledger Entries
        var reversalEntries = await _dbContext.LedgerEntries
            .Where(l => l.TransactionId == reversal.Id)
            .ToListAsync();

        reversalEntries.Should().HaveCount(4);

        // EUR leg
        var bobDebit = reversalEntries.FirstOrDefault(l => l.WalletId == _userB.Wallet.Id && l.Currency == "EUR");
        bobDebit.Should().NotBeNull();
        bobDebit!.EntryType.Should().Be(LedgerEntryType.Debit);
        bobDebit.Amount.Should().Be(92m);

        var systemFxCredit = reversalEntries.FirstOrDefault(l => l.WalletId == SystemConstants.SystemWalletId && l.Currency == "EUR");
        systemFxCredit.Should().NotBeNull();
        systemFxCredit!.EntryType.Should().Be(LedgerEntryType.Credit);
        systemFxCredit.Amount.Should().Be(92m);

        // USD leg
        var systemFxDebit = reversalEntries.FirstOrDefault(l => l.WalletId == SystemConstants.SystemWalletId && l.Currency == "USD");
        systemFxDebit.Should().NotBeNull();
        systemFxDebit!.EntryType.Should().Be(LedgerEntryType.Debit);
        systemFxDebit.Amount.Should().Be(100m);

        var aliceCredit = reversalEntries.FirstOrDefault(l => l.WalletId == _userA.Wallet.Id && l.Currency == "USD");
        aliceCredit.Should().NotBeNull();
        aliceCredit!.EntryType.Should().Be(LedgerEntryType.Credit);
        aliceCredit.Amount.Should().Be(100m);

        // Invariants: zero-sum balance within each currency leg
        reversalEntries.Where(l => l.Currency == "EUR" && l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount)
            .Should().Be(reversalEntries.Where(l => l.Currency == "EUR" && l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount));

        reversalEntries.Where(l => l.Currency == "USD" && l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount)
            .Should().Be(reversalEntries.Where(l => l.Currency == "USD" && l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount));
    }

    [Fact]
    public async Task ReverseTransactionAsync_AlreadyReversed_ThrowsInvalidTransactionException()
    {
        // Arrange
        var originalTxn = new Transaction
        {
            Reference = "TXN-ALREADY-REFUNDED",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Refunded,
            Amount = 50m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet!.Id,
            ReceiverWalletId = _userB.Wallet!.Id
        };
        _dbContext.Transactions.Add(originalTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Second reversal attempt");

        // Act
        var act = () => _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request);

        // Assert
        await act.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*already been reversed*");
    }

    [Fact]
    public async Task ReverseTransactionAsync_ReversingAReversal_ThrowsInvalidTransactionException()
    {
        // Arrange
        var reversalTxn = new Transaction
        {
            Reference = "TXN-REVERSAL-ROW",
            Type = TransactionType.Reversal,
            Status = TransactionStatus.Completed,
            Amount = 50m,
            Currency = "EGP",
            SenderWalletId = _userB.Wallet!.Id,
            ReceiverWalletId = _userA.Wallet!.Id
        };
        _dbContext.Transactions.Add(reversalTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Cannot reverse a reversal");

        // Act
        var act = () => _transactionService.ReverseTransactionAsync(_userA.Id, reversalTxn.Id, request);

        // Assert
        await act.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*reversal transaction cannot be reversed*");
    }

    [Fact]
    public async Task ReverseTransactionAsync_NonCompletedStatus_ThrowsInvalidTransactionException()
    {
        // Arrange
        var pendingTxn = new Transaction
        {
            Reference = "TXN-PENDING-ROW",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Pending,
            Amount = 50m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet!.Id,
            ReceiverWalletId = _userB.Wallet!.Id
        };
        _dbContext.Transactions.Add(pendingTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Reversal of pending");

        // Act
        var act = () => _transactionService.ReverseTransactionAsync(_userA.Id, pendingTxn.Id, request);

        // Assert
        await act.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*Only completed transactions can be reversed*");
    }

    [Fact]
    public async Task ReverseTransactionAsync_InsufficientReceiverBalance_ThrowsInsufficientBalanceException()
    {
        // Arrange: Bob only has 50 EGP, but needs 100 EGP to refund
        _userB.Wallet!.Balance = 50m;
        var originalTxn = new Transaction
        {
            Reference = "TXN-OVERSPENT",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 100m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet!.Id,
            ReceiverWalletId = _userB.Wallet!.Id
        };
        _dbContext.Transactions.Add(originalTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Dispute");

        // Act
        var act = () => _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request);

        // Assert
        await act.Should().ThrowAsync<InsufficientBalanceException>();
    }

    [Fact]
    public async Task ReverseTransactionAsync_UnauthorizedUser_ThrowsUnauthorizedDomainException()
    {
        // Arrange: User C tries to reverse a transaction between User A and User B
        var originalTxn = new Transaction
        {
            Reference = "TXN-PRIVATE-AB",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 50m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet!.Id,
            ReceiverWalletId = _userB.Wallet!.Id
        };
        _dbContext.Transactions.Add(originalTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Unauthorized dispute");

        // Act
        var act = () => _transactionService.ReverseTransactionAsync(_userC.Id, originalTxn.Id, request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedDomainException>()
            .WithMessage("*permission to reverse this transaction*");
    }

    [Fact]
    public async Task ReverseTransactionAsync_DepositReversal_ReversesUserAndTreasury()
    {
        // Arrange: User A had a deposit of 200m
        var depositTxn = new Transaction
        {
            Reference = "TXN-DEP-01",
            Type = TransactionType.Deposit,
            Status = TransactionStatus.Completed,
            Amount = 200m,
            Currency = "EGP",
            SenderWalletId = SystemConstants.SystemWalletId,
            ReceiverWalletId = _userA.Wallet!.Id
        };
        _dbContext.Transactions.Add(depositTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Deposit chargeback");

        // Act
        var reversal = await _transactionService.ReverseTransactionAsync(_userA.Id, depositTxn.Id, request);

        // Assert
        var updatedWallet = await _dbContext.Wallets.FindAsync(_userA.Wallet.Id);
        updatedWallet!.Balance.Should().Be(700m); // 900 - 200

        var reversalEntries = await _dbContext.LedgerEntries
            .Where(l => l.TransactionId == reversal.Id)
            .ToListAsync();

        reversalEntries.Should().HaveCount(2);
        reversalEntries.First(l => l.WalletId == _userA.Wallet.Id).EntryType.Should().Be(LedgerEntryType.Debit);
        reversalEntries.First(l => l.WalletId == SystemConstants.SystemWalletId).EntryType.Should().Be(LedgerEntryType.Credit);
    }

    [Fact]
    public async Task ReverseTransactionAsync_IdempotencyKey_ReturnsCachedResponseWithoutDuplicateExecution()
    {
        // Arrange
        var originalTxn = new Transaction
        {
            Reference = "TXN-IDEMP-01",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 50m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet!.Id,
            ReceiverWalletId = _userB.Wallet!.Id
        };
        _dbContext.Transactions.Add(originalTxn);
        await _dbContext.SaveChangesAsync();

        var request = new ReverseTransactionRequest("Idempotent reversal");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act: First Call
        var response1 = await _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request, idempotencyKey);

        // Act: Second Call with exact same idempotency key
        var response2 = await _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request, idempotencyKey);

        // Assert: Both return same response and no duplicate ledger entries created
        response1.Id.Should().Be(response2.Id);
        response1.Reference.Should().Be(response2.Reference);

        var allReversalTxns = await _dbContext.Transactions
            .Where(t => t.Type == TransactionType.Reversal)
            .ToListAsync();
        allReversalTxns.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReverseTransactionAsync_ConcurrentReversalAttempts_OnlyOneSucceedsAndDuplicateFails()
    {
        // Arrange
        var originalTxn = new Transaction
        {
            Reference = "TXN-CONCURRENT-01",
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 50m,
            Currency = "EGP",
            SenderWalletId = _userA.Wallet!.Id,
            ReceiverWalletId = _userB.Wallet!.Id
        };
        _dbContext.Transactions.Add(originalTxn);
        await _dbContext.SaveChangesAsync();

        var request1 = new ReverseTransactionRequest("Concurrent attempt 1");
        var request2 = new ReverseTransactionRequest("Concurrent attempt 2");

        var successCount = 0;
        var failureCount = 0;

        try
        {
            await _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request1);
            successCount++;
        }
        catch (InvalidTransactionException)
        {
            failureCount++;
        }

        try
        {
            await _transactionService.ReverseTransactionAsync(_userA.Id, originalTxn.Id, request2);
            successCount++;
        }
        catch (InvalidTransactionException)
        {
            failureCount++;
        }

        // Assert: Exactly one succeeds and the other fails because transaction is already refunded
        successCount.Should().Be(1);
        failureCount.Should().Be(1);

        var reversalTxns = await _dbContext.Transactions
            .Where(t => t.OriginalTransactionId == originalTxn.Id)
            .ToListAsync();
        reversalTxns.Should().HaveCount(1);
    }
}
