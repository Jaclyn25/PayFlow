using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.Common.Options;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class TransactionFeeIntegrationTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<IAuditLogService> _auditLogServiceMock = new();
    private readonly Mock<IExchangeRateService> _exchangeRateServiceMock = new();
    private readonly FeeService _feeService;
    private readonly TransferService _transferService;
    private readonly TransactionService _transactionService;

    private readonly User _senderUser;
    private readonly User _receiverUser;
    private readonly Wallet _systemTreasuryWallet;

    public TransactionFeeIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

        // Configure fee: 1% + 0.30 fixed fee
        var feeOptions = Options.Create(new FeeOptions
        {
            Enabled = true,
            Percentage = 0.01m,
            FixedFee = 0.30m,
            CapAmount = 50.00m,
            MinFee = 0.00m
        });
        _feeService = new FeeService(feeOptions);

        _senderUser = new User
        {
            FirstName = "Alice",
            LastName = "Smith",
            Email = "alice@payflow.local"
        };
        _senderUser.Wallet = new Wallet
        {
            UserId = _senderUser.Id,
            Balance = 1000.00m,
            Currency = "EGP"
        };

        _receiverUser = new User
        {
            FirstName = "Bob",
            LastName = "Jones",
            Email = "bob@payflow.local"
        };
        _receiverUser.Wallet = new Wallet
        {
            UserId = _receiverUser.Id,
            Balance = 500.00m,
            Currency = "EGP"
        };

        _systemTreasuryWallet = new Wallet
        {
            Id = SystemConstants.SystemWalletId,
            UserId = SystemConstants.SystemUserId,
            Balance = 0.00m,
            Currency = "EGP"
        };

        _dbContext.Users.AddRange(_senderUser, _receiverUser);
        _dbContext.Wallets.AddRange(_senderUser.Wallet, _receiverUser.Wallet, _systemTreasuryWallet);
        _dbContext.SaveChanges();

        _transferService = new TransferService(
            _dbContext,
            _cacheServiceMock.Object,
            _auditLogServiceMock.Object,
            _exchangeRateServiceMock.Object,
            _feeService);

        _transactionService = new TransactionService(_dbContext);
    }

    [Fact]
    public async Task TransferAsync_WithFee_DebitsSenderGrossAmountAndCreditsTreasuryFee()
    {
        // Arrange: Transfer 100 EGP. Fee = 100 * 0.01 + 0.30 = 1.30 EGP.
        // Gross debit = 101.30 EGP. Net receiver = 100.00 EGP. Fee = 1.30 EGP.
        var request = new TransferRequest(_receiverUser.Email, 100.00m, "Payment with fee");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var result = await _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert - Response Breakdown
        result.Should().NotBeNull();
        result.Amount.Should().Be(100.00m);
        result.FeeAmount.Should().Be(1.30m);
        result.GrossAmount.Should().Be(101.30m);
        result.NetAmount.Should().Be(100.00m);

        // Assert - Wallet Balances
        var updatedSenderWallet = await _dbContext.Wallets.FindAsync(_senderUser.Wallet!.Id);
        var updatedReceiverWallet = await _dbContext.Wallets.FindAsync(_receiverUser.Wallet!.Id);
        var updatedTreasuryWallet = await _dbContext.Wallets.FindAsync(SystemConstants.SystemWalletId);

        updatedSenderWallet!.Balance.Should().Be(898.70m); // 1000.00 - 101.30
        updatedReceiverWallet!.Balance.Should().Be(600.00m); // 500.00 + 100.00
        updatedTreasuryWallet!.Balance.Should().Be(1.30m); // 0.00 + 1.30 (treasury accumulates fees)

        // Assert - Double-Entry Ledger Entries
        var ledgerEntries = await _dbContext.LedgerEntries
            .Where(l => l.TransactionId == result.Id)
            .ToListAsync();

        ledgerEntries.Should().HaveCount(3); // Sender Debit, Receiver Credit, Treasury Credit

        var senderDebit = ledgerEntries.FirstOrDefault(l => l.WalletId == _senderUser.Wallet.Id);
        senderDebit.Should().NotBeNull();
        senderDebit!.EntryType.Should().Be(LedgerEntryType.Debit);
        senderDebit.Amount.Should().Be(101.30m);
        senderDebit.BalanceAfter.Should().Be(898.70m);

        var receiverCredit = ledgerEntries.FirstOrDefault(l => l.WalletId == _receiverUser.Wallet.Id);
        receiverCredit.Should().NotBeNull();
        receiverCredit!.EntryType.Should().Be(LedgerEntryType.Credit);
        receiverCredit.Amount.Should().Be(100.00m);
        receiverCredit.BalanceAfter.Should().Be(600.00m);

        var treasuryCredit = ledgerEntries.FirstOrDefault(l => l.WalletId == SystemConstants.SystemWalletId);
        treasuryCredit.Should().NotBeNull();
        treasuryCredit!.EntryType.Should().Be(LedgerEntryType.Credit);
        treasuryCredit.Amount.Should().Be(1.30m);
        treasuryCredit.BalanceAfter.Should().Be(1.30m);

        // Invariant: Total Debits == Total Credits (101.30 == 100.00 + 1.30)
        var totalDebits = ledgerEntries.Where(l => l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var totalCredits = ledgerEntries.Where(l => l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);
        totalDebits.Should().Be(totalCredits);
    }

    [Fact]
    public async Task TransferAsync_InsufficientBalanceForGrossAmount_ThrowsInsufficientBalanceException()
    {
        // Arrange: Sender only has 100.50, but transfer of 100 requires 101.30 with fee
        _senderUser.Wallet!.Balance = 100.50m;
        await _dbContext.SaveChangesAsync();

        var request = new TransferRequest(_receiverUser.Email, 100.00m, "Will fail on fee");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = () => _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        await act.Should().ThrowAsync<InsufficientBalanceException>();
    }

    [Fact]
    public async Task TransferAsync_CrossCurrencyTransferWithFee_MaintainsDoubleEntryBalanceAcrossBothCurrencies()
    {
        // Arrange: Sender (USD, 1000 balance), Receiver (EUR, 0 balance)
        _senderUser.Wallet!.Currency = "USD";
        _receiverUser.Wallet!.Currency = "EUR";
        _receiverUser.Wallet.Balance = 0.00m;
        await _dbContext.SaveChangesAsync();

        _exchangeRateServiceMock
            .Setup(x => x.GetExchangeRateAsync("USD", "EUR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.92m);

        // Transfer 100 USD @ 0.92 = 92 EUR.
        // Fee in USD = 100 * 0.01 + 0.30 = 1.30 USD.
        // Sender gross debit = 101.30 USD.
        var request = new TransferRequest(_receiverUser.Email, 100.00m, "Cross-currency with fee");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var result = await _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        result.Amount.Should().Be(100.00m);
        result.FeeAmount.Should().Be(1.30m);
        result.GrossAmount.Should().Be(101.30m);
        result.TargetAmount.Should().Be(92.00m);
        result.TargetCurrency.Should().Be("EUR");

        var updatedSenderWallet = await _dbContext.Wallets.FindAsync(_senderUser.Wallet.Id);
        var updatedReceiverWallet = await _dbContext.Wallets.FindAsync(_receiverUser.Wallet.Id);
        var updatedTreasuryWallet = await _dbContext.Wallets.FindAsync(SystemConstants.SystemWalletId);

        updatedSenderWallet!.Balance.Should().Be(898.70m); // 1000 - 101.30 USD
        updatedReceiverWallet!.Balance.Should().Be(92.00m); // 0 + 92 EUR
        updatedTreasuryWallet!.Balance.Should().Be(1.30m); // 0 + 1.30 USD

        // Assert - Ledger Entries across currencies
        var entries = await _dbContext.LedgerEntries
            .Where(l => l.TransactionId == result.Id)
            .ToListAsync();

        entries.Should().HaveCount(5); // Sender Debit, FX Credit, Treasury Credit, FX Debit, Receiver Credit

        // USD Leg
        var usdDebits = entries.Where(l => l.Currency == "USD" && l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var usdCredits = entries.Where(l => l.Currency == "USD" && l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);
        usdDebits.Should().Be(101.30m);
        usdCredits.Should().Be(101.30m);

        // EUR Leg
        var eurDebits = entries.Where(l => l.Currency == "EUR" && l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var eurCredits = entries.Where(l => l.Currency == "EUR" && l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);
        eurDebits.Should().Be(92.00m);
        eurCredits.Should().Be(92.00m);
    }

    [Fact]
    public async Task ReverseTransactionAsync_TransferWithFee_RefundsPrincipalAndFeeToSenderAndDebitsTreasury()
    {
        // Arrange: First execute a transfer with fee
        var request = new TransferRequest(_receiverUser.Email, 100.00m, "Transfer to be reversed");
        var transferTxn = await _transferService.TransferAsync(_senderUser.Id, Guid.NewGuid().ToString(), request);

        // Act: Reverse transaction
        var reverseRequest = new ReverseTransactionRequest("Full refund including fee");
        var reversalResult = await _transactionService.ReverseTransactionAsync(_senderUser.Id, transferTxn.Id, reverseRequest);

        // Assert - Balances fully restored
        var updatedSenderWallet = await _dbContext.Wallets.FindAsync(_senderUser.Wallet!.Id);
        var updatedReceiverWallet = await _dbContext.Wallets.FindAsync(_receiverUser.Wallet!.Id);
        var updatedTreasuryWallet = await _dbContext.Wallets.FindAsync(SystemConstants.SystemWalletId);

        updatedSenderWallet!.Balance.Should().Be(1000.00m); // Principal (100) + Fee (1.30) returned
        updatedReceiverWallet!.Balance.Should().Be(500.00m);  // 100 deducted
        updatedTreasuryWallet!.Balance.Should().Be(0.00m);    // Fee deducted back from treasury

        // Assert - Compensating Ledger Entries
        var reversalEntries = await _dbContext.LedgerEntries
            .Where(l => l.TransactionId == reversalResult.Id)
            .ToListAsync();

        reversalEntries.Should().HaveCount(3); // Receiver Debit (100), Treasury Debit (1.30), Sender Credit (101.30)

        var totalDebits = reversalEntries.Where(l => l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var totalCredits = reversalEntries.Where(l => l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);
        totalDebits.Should().Be(totalCredits);
        totalDebits.Should().Be(101.30m);
    }
}
