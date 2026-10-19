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
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class AdminServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<IAuditLogService> _auditLogServiceMock = new();
    private readonly AdminService _adminService;

    private readonly User _customerUser;
    private readonly User _complianceUser;
    private readonly User _adminUser;

    public AdminServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(dbOptions);

        _adminService = new AdminService(_dbContext, _cacheServiceMock.Object, _auditLogServiceMock.Object);

        _customerUser = new User
        {
            FirstName = "Charlie",
            LastName = "Customer",
            Email = "charlie@customer.local",
            Role = UserRole.Customer,
            KycTier = KycTier.Tier1,
            IsActive = true
        };
        _customerUser.Wallet = new Wallet { UserId = _customerUser.Id, Balance = 1000m, Currency = "EGP" };

        _complianceUser = new User
        {
            FirstName = "Oscar",
            LastName = "Officer",
            Email = "oscar@compliance.local",
            Role = UserRole.ComplianceOfficer,
            KycTier = KycTier.Tier2,
            IsActive = true
        };
        _complianceUser.Wallet = new Wallet { UserId = _complianceUser.Id, Balance = 500m, Currency = "EGP" };

        _adminUser = new User
        {
            FirstName = "Arthur",
            LastName = "Admin",
            Email = "arthur@admin.local",
            Role = UserRole.SystemAdmin,
            KycTier = KycTier.Tier3,
            IsActive = true
        };
        _adminUser.Wallet = new Wallet { UserId = _adminUser.Id, Balance = 2000m, Currency = "EGP" };

        _dbContext.Users.AddRange(_customerUser, _complianceUser, _adminUser);
        _dbContext.Wallets.AddRange(_customerUser.Wallet, _complianceUser.Wallet, _adminUser.Wallet);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task FreezeWalletAsync_ValidWallet_FreezesWalletAndEvictsCache()
    {
        // Act
        var result = await _adminService.FreezeWalletAsync(_customerUser.Wallet!.Id, "Suspicious rapid transactions");

        // Assert
        result.Should().NotBeNull();
        result.WalletId.Should().Be(_customerUser.Wallet.Id);
        result.IsFrozen.Should().BeTrue();
        result.FreezeReason.Should().Be("Suspicious rapid transactions");
        result.FrozenAtUtc.Should().NotBeNull();

        // Check DB state
        var walletInDb = await _dbContext.Wallets.FindAsync(_customerUser.Wallet.Id);
        walletInDb!.IsFrozen.Should().BeTrue();
        walletInDb.FreezeReason.Should().Be("Suspicious rapid transactions");

        // Verify cache eviction
        _cacheServiceMock.Verify(c => c.RemoveAsync($"wallet:user:{_customerUser.Id}", It.IsAny<CancellationToken>()), Times.Once);

        // Verify audit log
        _auditLogServiceMock.Verify(a => a.LogAsync(
            _customerUser.Id,
            "WalletFrozen",
            _customerUser.Wallet.Id.ToString(),
            true,
            "Suspicious rapid transactions",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FreezeWalletAsync_NonExistentWallet_ThrowsWalletNotFoundException()
    {
        // Act
        var act = () => _adminService.FreezeWalletAsync(Guid.NewGuid(), "Reason");

        // Assert
        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task UnfreezeWalletAsync_FrozenWallet_UnfreezesWalletAndEvictsCache()
    {
        // Arrange
        _customerUser.Wallet!.Freeze("Investigation");
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _adminService.UnfreezeWalletAsync(_customerUser.Wallet.Id);

        // Assert
        result.IsFrozen.Should().BeFalse();
        result.FreezeReason.Should().BeNull();
        result.FrozenAtUtc.Should().BeNull();

        var walletInDb = await _dbContext.Wallets.FindAsync(_customerUser.Wallet.Id);
        walletInDb!.IsFrozen.Should().BeFalse();
        walletInDb.FreezeReason.Should().BeNull();

        _cacheServiceMock.Verify(c => c.RemoveAsync($"wallet:user:{_customerUser.Id}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUsersAsync_FilterByRole_ReturnsOnlyMatchingUsers()
    {
        // Act
        var paramsFilter = new AdminUserFilterParams(Role: "ComplianceOfficer");
        var result = await _adminService.GetUsersAsync(paramsFilter);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Email.Should().Be("oscar@compliance.local");
        result.Items[0].Role.Should().Be("ComplianceOfficer");
    }

    [Fact]
    public async Task GetUsersAsync_SearchKeyword_ReturnsMatchingUsers()
    {
        // Act
        var paramsFilter = new AdminUserFilterParams(Search: "charlie");
        var result = await _adminService.GetUsersAsync(paramsFilter);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].FirstName.Should().Be("Charlie");
    }

    [Fact]
    public async Task UpdateUserRoleAsync_ValidUser_UpdatesRoleInDbAndAudits()
    {
        // Act
        var result = await _adminService.UpdateUserRoleAsync(_customerUser.Id, UserRole.ComplianceOfficer);

        // Assert
        result.Role.Should().Be("ComplianceOfficer");

        var userInDb = await _dbContext.Users.FindAsync(_customerUser.Id);
        userInDb!.Role.Should().Be(UserRole.ComplianceOfficer);

        _auditLogServiceMock.Verify(a => a.LogAsync(
            _customerUser.Id,
            "UserRoleUpdated",
            _customerUser.Id.ToString(),
            true,
            "Role set to ComplianceOfficer",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLedgerReconciliationAsync_BalancedEntries_ReturnsIsReconciledTrue()
    {
        // Arrange: Create a balanced deposit transaction
        var txn = new Transaction
        {
            Reference = "TX-RECON-01",
            Type = TransactionType.Deposit,
            Status = TransactionStatus.Completed,
            Amount = 500m,
            Currency = "EGP"
        };
        var systemDebit = new LedgerEntry
        {
            TransactionId = txn.Id,
            WalletId = SystemConstants.SystemWalletId,
            EntryType = LedgerEntryType.Debit,
            Amount = 500m,
            Currency = "EGP"
        };
        var userCredit = new LedgerEntry
        {
            TransactionId = txn.Id,
            WalletId = _customerUser.Wallet!.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 500m,
            Currency = "EGP"
        };

        _dbContext.Transactions.Add(txn);
        _dbContext.LedgerEntries.AddRange(systemDebit, userCredit);
        await _dbContext.SaveChangesAsync();

        // Act
        var recon = await _adminService.GetLedgerReconciliationAsync();

        // Assert
        recon.TotalPlatformCredits.Should().Be(500m);
        recon.TotalPlatformDebits.Should().Be(500m);
        recon.IsReconciled.Should().BeTrue();
        recon.TotalWalletsCount.Should().Be(3);
        recon.TotalCompletedTransactions.Should().Be(1);
        recon.SystemTreasuryBalances.Should().ContainKey("EGP");
    }

    [Fact]
    public async Task GetWalletsAsync_FilterByFrozen_ReturnsOnlyFrozenWallets()
    {
        // Arrange
        _customerUser.Wallet!.Freeze("Audit");
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _adminService.GetWalletsAsync(page: 1, pageSize: 10, isFrozen: true);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(_customerUser.Wallet.Id);
        result.Items[0].IsFrozen.Should().BeTrue();
        result.Items[0].FreezeReason.Should().Be("Audit");
    }
}
