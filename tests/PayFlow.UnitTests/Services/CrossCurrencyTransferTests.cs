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

namespace PayFlow.UnitTests.Services;

public class CrossCurrencyTransferTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<IAuditLogService> _auditLogServiceMock = new();
    private readonly Mock<IExchangeRateService> _exchangeRateServiceMock = new();
    private readonly TransferService _transferService;

    private readonly User _senderUsdUser;
    private readonly User _receiverEgpUser;

    public CrossCurrencyTransferTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

        // Sender wallet in USD with $100 balance
        _senderUsdUser = new User
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.usd@payflow.local"
        };
        _senderUsdUser.Wallet = new Wallet
        {
            UserId = _senderUsdUser.Id,
            Balance = 100.00m,
            Currency = CurrencyConstants.Usd
        };

        // Receiver wallet in EGP with 500 EGP balance
        _receiverEgpUser = new User
        {
            FirstName = "Omar",
            LastName = "Hassan",
            Email = "omar.egp@payflow.local"
        };
        _receiverEgpUser.Wallet = new Wallet
        {
            UserId = _receiverEgpUser.Id,
            Balance = 500.00m,
            Currency = CurrencyConstants.Egp
        };

        _dbContext.Users.AddRange(_senderUsdUser, _receiverEgpUser);
        _dbContext.Wallets.AddRange(_senderUsdUser.Wallet, _receiverEgpUser.Wallet);
        _dbContext.SaveChanges();

        // 1 USD = 48.50 EGP
        _exchangeRateServiceMock.Setup(x => x.GetExchangeRateAsync("USD", "EGP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(48.50m);

        _transferService = new TransferService(
            _dbContext,
            _cacheServiceMock.Object,
            _auditLogServiceMock.Object,
            _exchangeRateServiceMock.Object);
    }

    [Fact]
    public async Task TransferAsync_CrossCurrencyUsdToEgp_DeductsUsdCreditsConvertedEgpAndMaintainsBalancedLedger()
    {
        // Arrange: Transfer $10 USD to EGP receiver
        var request = new TransferRequest("omar.egp@payflow.local", 10.00m, "Cross-border payment");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var result = await _transferService.TransferAsync(_senderUsdUser.Id, idempotencyKey, request);

        // Assert: Response
        result.Should().NotBeNull();
        result.Amount.Should().Be(10.00m);
        result.Currency.Should().Be("USD");
        result.ExchangeRate.Should().Be(48.50m);
        result.TargetAmount.Should().Be(485.00m);
        result.TargetCurrency.Should().Be("EGP");

        // Assert: Wallets
        var updatedSenderWallet = await _dbContext.Wallets.FindAsync(_senderUsdUser.Wallet!.Id);
        var updatedReceiverWallet = await _dbContext.Wallets.FindAsync(_receiverEgpUser.Wallet!.Id);

        updatedSenderWallet!.Balance.Should().Be(90.00m); // 100 - 10
        updatedReceiverWallet!.Balance.Should().Be(985.00m); // 500 + 485

        // Assert: Double-Entry Balanced Ledger Entries
        var ledgers = await _dbContext.LedgerEntries
            .Where(l => l.TransactionId == result.Id)
            .ToListAsync();

        ledgers.Should().HaveCount(4);

        // In USD: Sum(Debit) == Sum(Credit)
        var usdDebits = ledgers.Where(l => l.Currency == "USD" && l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var usdCredits = ledgers.Where(l => l.Currency == "USD" && l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);
        usdDebits.Should().Be(10.00m);
        usdCredits.Should().Be(10.00m);

        // In EGP: Sum(Debit) == Sum(Credit)
        var egpDebits = ledgers.Where(l => l.Currency == "EGP" && l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var egpCredits = ledgers.Where(l => l.Currency == "EGP" && l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);
        egpDebits.Should().Be(485.00m);
        egpCredits.Should().Be(485.00m);

        // Assert: Outbox message contains rate & target info
        var outboxMessage = await _dbContext.OutboxMessages.FirstOrDefaultAsync();
        outboxMessage.Should().NotBeNull();
        outboxMessage!.Payload.Should().Contain("48.5");
        outboxMessage.Payload.Should().Contain("485");
        outboxMessage.Payload.Should().Contain("EGP");
    }
}
