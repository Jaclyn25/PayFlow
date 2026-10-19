using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Options;
using PayFlow.Application.Services;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class KycLimitServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly KycLimitService _kycLimitService;
    private readonly KycOptions _options;
    private readonly User _user;
    private readonly Wallet _wallet;

    public KycLimitServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(dbOptions);

        _options = new KycOptions
        {
            TierLimits = new Dictionary<string, KycTierLimitConfig>
            {
                [KycTier.Unverified.ToString()] = new() { MaxSingleTransaction = 500m, DailyLimit = 1000m, MonthlyLimit = 5000m },
                [KycTier.Tier1.ToString()] = new() { MaxSingleTransaction = 5000m, DailyLimit = 10000m, MonthlyLimit = 50000m },
                [KycTier.Tier2.ToString()] = new() { MaxSingleTransaction = 50000m, DailyLimit = 100000m, MonthlyLimit = 500000m },
                [KycTier.Tier3.ToString()] = new() { MaxSingleTransaction = 250000m, DailyLimit = 1000000m, MonthlyLimit = 5000000m }
            }
        };

        _kycLimitService = new KycLimitService(_dbContext, Options.Create(_options));

        _user = new User
        {
            FirstName = "Alice",
            LastName = "Smith",
            Email = "alice@payflow.local",
            KycTier = KycTier.Tier1
        };

        _wallet = new Wallet
        {
            UserId = _user.Id,
            Balance = 100000m,
            Currency = "EGP"
        };
        _user.Wallet = _wallet;

        _dbContext.Users.Add(_user);
        _dbContext.Wallets.Add(_wallet);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task ValidateTransferLimitsAsync_WithinLimits_DoesNotThrow()
    {
        // Act
        var act = () => _kycLimitService.ValidateTransferLimitsAsync(_user, _wallet.Id, 2000m, "EGP");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateTransferLimitsAsync_ExceedsSingleTransferLimit_ThrowsKycLimitExceededException()
    {
        // Tier 1 single limit is 5,000
        var act = () => _kycLimitService.ValidateTransferLimitsAsync(_user, _wallet.Id, 5001m, "EGP");

        var exception = await act.Should().ThrowAsync<KycLimitExceededException>();
        exception.Which.CurrentTier.Should().Be(KycTier.Tier1);
        exception.Which.LimitType.Should().Be("SingleTransaction");
        exception.Which.LimitAmount.Should().Be(5000m);
        exception.Which.CurrentUsage.Should().Be(5001m);
        exception.Which.RequiredAction.Should().Contain("Tier 2");
    }

    [Fact]
    public async Task ValidateTransferLimitsAsync_ExceedsDailyLimit_ThrowsKycLimitExceededException()
    {
        // Arrange: User has already sent 8,000 in past 2 hours (Tier 1 daily limit is 10,000)
        var priorTx = new Transaction
        {
            Reference = "TX-PRIOR-01",
            SenderWalletId = _wallet.Id,
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 8000m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        _dbContext.Transactions.Add(priorTx);
        await _dbContext.SaveChangesAsync();

        // Act: Attempt to send 2,500 (total would be 10,500 > 10,000)
        var act = () => _kycLimitService.ValidateTransferLimitsAsync(_user, _wallet.Id, 2500m, "EGP");

        // Assert
        var exception = await act.Should().ThrowAsync<KycLimitExceededException>();
        exception.Which.LimitType.Should().Be("DailyLimit24h");
        exception.Which.CurrentUsage.Should().Be(8000m);
        exception.Which.LimitAmount.Should().Be(10000m);
        exception.Which.RequiredAction.Should().Contain("Tier 2");
    }

    [Fact]
    public async Task ValidateTransferLimitsAsync_ExceedsMonthlyLimit_ThrowsKycLimitExceededException()
    {
        // Arrange: User has sent 48,000 5 days ago (Tier 1 monthly limit is 50,000)
        var priorTx = new Transaction
        {
            Reference = "TX-PRIOR-02",
            SenderWalletId = _wallet.Id,
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 48000m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        _dbContext.Transactions.Add(priorTx);
        await _dbContext.SaveChangesAsync();

        // Act: Attempt to send 3,000 (total would be 51,000 > 50,000)
        var act = () => _kycLimitService.ValidateTransferLimitsAsync(_user, _wallet.Id, 3000m, "EGP");

        // Assert
        var exception = await act.Should().ThrowAsync<KycLimitExceededException>();
        exception.Which.LimitType.Should().Be("MonthlyLimit30d");
        exception.Which.CurrentUsage.Should().Be(48000m);
        exception.Which.LimitAmount.Should().Be(50000m);
        exception.Which.RequiredAction.Should().Contain("Tier 2");
    }

    [Fact]
    public async Task ValidateTransferLimitsAsync_TransactionsOlderThanWindows_Ignored()
    {
        // 25 hours ago: outside 24-hour daily window
        var oldDailyTx = new Transaction
        {
            Reference = "TX-OLD-DAILY",
            SenderWalletId = _wallet.Id,
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 9000m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-25)
        };

        // 35 days ago: outside 30-day monthly window
        var oldMonthlyTx = new Transaction
        {
            Reference = "TX-OLD-MONTHLY",
            SenderWalletId = _wallet.Id,
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 45000m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-35)
        };

        _dbContext.Transactions.AddRange(oldDailyTx, oldMonthlyTx);
        await _dbContext.SaveChangesAsync();

        // Act: Sending 4,000 should succeed since older transactions don't count towards current daily/monthly windows
        var act = () => _kycLimitService.ValidateTransferLimitsAsync(_user, _wallet.Id, 4000m, "EGP");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateTransferLimitsAsync_NonTransferOrFailedTransactions_NotCounted()
    {
        // Failed transfer
        var failedTx = new Transaction
        {
            Reference = "TX-FAILED",
            SenderWalletId = _wallet.Id,
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Failed,
            Amount = 9000m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        // Deposit
        var depositTx = new Transaction
        {
            Reference = "TX-DEPOSIT",
            SenderWalletId = _wallet.Id,
            Type = TransactionType.Deposit,
            Status = TransactionStatus.Completed,
            Amount = 9000m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        _dbContext.Transactions.AddRange(failedTx, depositTx);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var act = () => _kycLimitService.ValidateTransferLimitsAsync(_user, _wallet.Id, 4000m, "EGP");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetUserLimitsAsync_CalculatesRemainingAllowancesAccurately()
    {
        // Arrange: Add a 3,000 transaction in last 24h
        var tx = new Transaction
        {
            Reference = "TX-LIMITS-01",
            SenderWalletId = _wallet.Id,
            Type = TransactionType.Transfer,
            Status = TransactionStatus.Completed,
            Amount = 3000m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-3)
        };
        _dbContext.Transactions.Add(tx);
        await _dbContext.SaveChangesAsync();

        // Act
        var limits = await _kycLimitService.GetUserLimitsAsync(_user.Id);

        // Assert
        limits.KycTier.Should().Be("Tier1");
        limits.MaxSingleTransaction.Should().Be(5000m);
        limits.DailyLimit.Should().Be(10000m);
        limits.CurrentDailyUsage.Should().Be(3000m);
        limits.RemainingDailyLimit.Should().Be(7000m);
        limits.MonthlyLimit.Should().Be(50000m);
        limits.CurrentMonthlyUsage.Should().Be(3000m);
        limits.RemainingMonthlyLimit.Should().Be(47000m);
    }

    [Fact]
    public async Task UpdateUserTierAsync_UpgradesTierAndIncreasesLimits()
    {
        // Act: Upgrade user to Tier 2
        var updatedProfile = await _kycLimitService.UpdateUserTierAsync(_user.Id, KycTier.Tier2);

        // Assert profile
        updatedProfile.KycTier.Should().Be("Tier2");

        // Assert DB
        var userInDb = await _dbContext.Users.FindAsync(_user.Id);
        userInDb!.KycTier.Should().Be(KycTier.Tier2);
        userInDb.KycVerifiedAtUtc.Should().NotBeNull();

        // Assert higher transfer limit is now permitted (e.g. 20,000 which would fail under Tier 1)
        var act = () => _kycLimitService.ValidateTransferLimitsAsync(userInDb, _wallet.Id, 20000m, "EGP");
        await act.Should().NotThrowAsync();
    }
}
