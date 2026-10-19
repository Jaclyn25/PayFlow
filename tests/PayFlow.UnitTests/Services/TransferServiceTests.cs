using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class TransferServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<IAuditLogService> _auditLogServiceMock = new();
    private readonly TransferService _transferService;

    private readonly User _senderUser;
    private readonly User _receiverUser;

    public TransferServiceTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

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

        _dbContext.Users.AddRange(_senderUser, _receiverUser);
        _dbContext.Wallets.AddRange(_senderUser.Wallet, _receiverUser.Wallet);
        _dbContext.SaveChanges();

        _transferService = new TransferService(_dbContext, _cacheServiceMock.Object, _auditLogServiceMock.Object);
    }

    [Fact]
    public async Task TransferAsync_ValidTransfer_UpdatesBalancesCreatesLedgerEntriesAndOutbox()
    {
        // Arrange
        var request = new TransferRequest("bob@payflow.local", 300.00m, "Dinner payment");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var result = await _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        result.Should().NotBeNull();
        result.Amount.Should().Be(300.00m);
        result.Status.Should().Be("Completed");

        var senderWallet = await _dbContext.Wallets.FindAsync(_senderUser.Wallet!.Id);
        var receiverWallet = await _dbContext.Wallets.FindAsync(_receiverUser.Wallet!.Id);

        senderWallet!.Balance.Should().Be(700.00m);
        receiverWallet!.Balance.Should().Be(800.00m);

        var ledgerEntries = await _dbContext.LedgerEntries.Where(l => l.TransactionId == result.Id).ToListAsync();
        ledgerEntries.Should().HaveCount(2);

        var debitEntry = ledgerEntries.Single(l => l.EntryType == LedgerEntryType.Debit);
        debitEntry.WalletId.Should().Be(_senderUser.Wallet.Id);
        debitEntry.Amount.Should().Be(300.00m);
        debitEntry.BalanceAfter.Should().Be(700.00m);

        var creditEntry = ledgerEntries.Single(l => l.EntryType == LedgerEntryType.Credit);
        creditEntry.WalletId.Should().Be(_receiverUser.Wallet.Id);
        creditEntry.Amount.Should().Be(300.00m);
        creditEntry.BalanceAfter.Should().Be(800.00m);

        var outboxMessage = await _dbContext.OutboxMessages.FirstOrDefaultAsync();
        outboxMessage.Should().NotBeNull();
        outboxMessage!.Type.Should().Be("TransferCompleted");
    }

    [Fact]
    public async Task TransferAsync_SelfTransfer_ThrowsInvalidTransactionException()
    {
        // Arrange
        var request = new TransferRequest("alice@payflow.local", 100.00m, "Self transfer");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = () => _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        await act.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*Self-transfer is not allowed*");
    }

    [Fact]
    public async Task TransferAsync_InsufficientBalance_ThrowsInsufficientBalanceException()
    {
        // Arrange
        var request = new TransferRequest("bob@payflow.local", 2000.00m, "Overspend");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = () => _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        await act.Should().ThrowAsync<InsufficientBalanceException>()
            .WithMessage("*Insufficient wallet balance*");
    }

    [Fact]
    public async Task TransferAsync_DuplicateIdempotencyKey_ReturnsCachedOriginalResponseWithoutDoubleDeducting()
    {
        // Arrange
        var request = new TransferRequest("bob@payflow.local", 200.00m, "Payment 1");
        var idempotencyKey = "UNIQUE_KEY_12345";

        // Act 1: Initial request
        var response1 = await _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Act 2: Duplicate request with SAME idempotency key
        var response2 = await _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        response2.Should().NotBeNull();
        response2.Id.Should().Be(response1.Id);
        response2.Reference.Should().Be(response1.Reference);

        var senderWallet = await _dbContext.Wallets.FindAsync(_senderUser.Wallet!.Id);
        senderWallet!.Balance.Should().Be(800.00m); // Deducted ONCE (1000 - 200 = 800), NOT twice (600)!
    }

    [Fact]
    public async Task TransferAsync_WhenKycLimitExceeded_ThrowsKycLimitExceededExceptionBeforeExecution()
    {
        // Arrange
        var kycMock = new Mock<IKycLimitService>();
        kycMock.Setup(k => k.ValidateTransferLimitsAsync(
            It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KycLimitExceededException(
                "Transfer limit exceeded",
                "SingleTransaction",
                5000m,
                6000m,
                "Upgrade to Tier 2",
                KycTier.Tier1));

        var transferServiceWithKyc = new TransferService(
            _dbContext,
            _cacheServiceMock.Object,
            _auditLogServiceMock.Object,
            null,
            null,
            kycMock.Object);

        var request = new TransferRequest("bob@payflow.local", 600.00m, "Payment");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = () => transferServiceWithKyc.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        var ex = await act.Should().ThrowAsync<KycLimitExceededException>();
        ex.Which.LimitType.Should().Be("SingleTransaction");

        // Verify balance was untouched
        var senderWallet = await _dbContext.Wallets.FindAsync(_senderUser.Wallet!.Id);
        senderWallet!.Balance.Should().Be(1000.00m);
    }

    [Fact]
    public async Task TransferAsync_WhenTwoFactorRequiredForHighValue_ThrowsTwoFactorRequiredException()
    {
        // Arrange
        var twoFactorMock = new Mock<ITwoFactorAuthService>();
        twoFactorMock.Setup(t => t.ValidateStepUpTransferAsync(
            It.IsAny<User>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TwoFactorRequiredException(5000m, 10000m));

        var transferServiceWith2Fa = new TransferService(
            _dbContext,
            _cacheServiceMock.Object,
            _auditLogServiceMock.Object,
            null,
            null,
            null,
            twoFactorMock.Object);

        var request = new TransferRequest("bob@payflow.local", 10000.00m, "Large transfer");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = () => transferServiceWith2Fa.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        var ex = await act.Should().ThrowAsync<TwoFactorRequiredException>();
        ex.Which.ThresholdAmount.Should().Be(5000m);
        ex.Which.AttemptedAmount.Should().Be(10000m);

        // Balance untouched
        var senderWallet = await _dbContext.Wallets.FindAsync(_senderUser.Wallet!.Id);
        senderWallet!.Balance.Should().Be(1000.00m);
    }

    [Fact]
    public async Task TransferAsync_WhenSenderWalletIsFrozen_ThrowsInvalidTransactionException()
    {
        // Arrange
        _senderUser.Wallet!.Freeze("Account flagged");
        await _dbContext.SaveChangesAsync();

        var request = new TransferRequest("bob@payflow.local", 100.00m, "Payment");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = () => _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        await act.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*Sender wallet is frozen*");
    }

    [Fact]
    public async Task TransferAsync_WhenReceiverWalletIsFrozen_ThrowsInvalidTransactionException()
    {
        // Arrange
        _receiverUser.Wallet!.Freeze("Under audit");
        await _dbContext.SaveChangesAsync();

        var request = new TransferRequest("bob@payflow.local", 100.00m, "Payment");
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var act = () => _transferService.TransferAsync(_senderUser.Id, idempotencyKey, request);

        // Assert
        await act.Should().ThrowAsync<InvalidTransactionException>()
            .WithMessage("*Receiver wallet is frozen*");
    }
}



