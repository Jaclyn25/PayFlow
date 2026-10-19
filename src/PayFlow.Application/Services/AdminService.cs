using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class AdminService : IAdminService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly IAuditLogService? _auditLogService;

    public AdminService(
        IPayFlowDbContext dbContext,
        ICacheService cacheService,
        IAuditLogService? auditLogService = null)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _auditLogService = auditLogService;
    }

    public async Task<WalletFreezeResponse> FreezeWalletAsync(
        Guid walletId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .Include(w => w.User)
            .FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);

        if (wallet == null)
        {
            throw new WalletNotFoundException(walletId.ToString());
        }

        wallet.Freeze(reason);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cached wallet response
        await _cacheService.RemoveAsync($"wallet:user:{wallet.UserId}", cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                wallet.UserId,
                "WalletFrozen",
                wallet.Id.ToString(),
                true,
                failureReason: reason,
                cancellationToken: cancellationToken);
        }

        return new WalletFreezeResponse(
            WalletId: wallet.Id,
            UserId: wallet.UserId,
            UserEmail: wallet.User?.Email ?? string.Empty,
            IsFrozen: wallet.IsFrozen,
            FreezeReason: wallet.FreezeReason,
            FrozenAtUtc: wallet.FrozenAtUtc
        );
    }

    public async Task<WalletFreezeResponse> UnfreezeWalletAsync(
        Guid walletId,
        CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .Include(w => w.User)
            .FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);

        if (wallet == null)
        {
            throw new WalletNotFoundException(walletId.ToString());
        }

        wallet.Unfreeze();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cached wallet response
        await _cacheService.RemoveAsync($"wallet:user:{wallet.UserId}", cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                wallet.UserId,
                "WalletUnfrozen",
                wallet.Id.ToString(),
                true,
                cancellationToken: cancellationToken);
        }

        return new WalletFreezeResponse(
            WalletId: wallet.Id,
            UserId: wallet.UserId,
            UserEmail: wallet.User?.Email ?? string.Empty,
            IsFrozen: wallet.IsFrozen,
            FreezeReason: wallet.FreezeReason,
            FrozenAtUtc: wallet.FrozenAtUtc
        );
    }

    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(
        AdminUserFilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Wallet)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(search) ||
                u.FirstName.ToLower().Contains(search) ||
                u.LastName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filterParams.Role) &&
            Enum.TryParse<UserRole>(filterParams.Role, true, out var roleEnum))
        {
            query = query.Where(u => u.Role == roleEnum);
        }

        if (!string.IsNullOrWhiteSpace(filterParams.KycTier) &&
            Enum.TryParse<KycTier>(filterParams.KycTier, true, out var tierEnum))
        {
            query = query.Where(u => u.KycTier == tierEnum);
        }

        if (filterParams.IsActive.HasValue)
        {
            query = query.Where(u => u.IsActive == filterParams.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, filterParams.Page);
        var pageSize = Math.Clamp(filterParams.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto(
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.Role.ToString(),
                u.KycTier.ToString(),
                u.KycVerifiedAtUtc,
                u.TwoFactorEnabled,
                u.IsActive,
                u.CreatedAtUtc,
                u.Wallet != null ? u.Wallet.Id : null,
                u.Wallet != null ? u.Wallet.Balance : 0m,
                u.Wallet != null ? u.Wallet.Currency : "EGP",
                u.Wallet != null && u.Wallet.IsFrozen
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<AdminUserDto>.Create(items, page, pageSize, totalCount);
    }

    public async Task<AdminUserDto> UpdateUserRoleAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        user.Role = role;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService != null)
        {
            await _auditLogService.LogAsync(
                userId,
                "UserRoleUpdated",
                user.Id.ToString(),
                true,
                failureReason: $"Role set to {role}",
                cancellationToken: cancellationToken);
        }

        return new AdminUserDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Role.ToString(),
            user.KycTier.ToString(),
            user.KycVerifiedAtUtc,
            user.TwoFactorEnabled,
            user.IsActive,
            user.CreatedAtUtc,
            user.Wallet?.Id,
            user.Wallet?.Balance ?? 0m,
            user.Wallet?.Currency ?? "EGP",
            user.Wallet?.IsFrozen ?? false
        );
    }

    public async Task<LedgerReconciliationResponse> GetLedgerReconciliationAsync(
        CancellationToken cancellationToken = default)
    {
        // 1. Total platform debits & credits across all ledger entries
        var totalCredits = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(l => l.EntryType == LedgerEntryType.Credit)
            .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;

        var totalDebits = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(l => l.EntryType == LedgerEntryType.Debit)
            .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;

        // Double-entry accounting balance invariant
        var isReconciled = totalCredits == totalDebits;

        // 2. System Treasury Balances per Currency
        var systemWalletEntries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(l => l.WalletId == SystemConstants.SystemWalletId)
            .GroupBy(l => new { l.Currency, l.EntryType })
            .Select(g => new
            {
                g.Key.Currency,
                g.Key.EntryType,
                Total = g.Sum(x => x.Amount)
            })
            .ToListAsync(cancellationToken);

        var treasuryBalances = new Dictionary<string, decimal>();
        var currencies = systemWalletEntries.Select(e => e.Currency).Distinct().ToList();

        if (!currencies.Contains(CurrencyConstants.Egp))
        {
            currencies.Add(CurrencyConstants.Egp);
        }

        foreach (var currency in currencies)
        {
            var credits = systemWalletEntries
                .Where(e => e.Currency == currency && e.EntryType == LedgerEntryType.Credit)
                .Sum(e => e.Total);

            var debits = systemWalletEntries
                .Where(e => e.Currency == currency && e.EntryType == LedgerEntryType.Debit)
                .Sum(e => e.Total);

            // System Treasury Net = Credits collected (e.g. fees) minus Debits issued (e.g. initial deposits)
            treasuryBalances[currency] = credits - debits;
        }

        // 3. Platform aggregates
        var totalWallets = await _dbContext.Wallets.CountAsync(cancellationToken);
        var frozenWallets = await _dbContext.Wallets.CountAsync(w => w.IsFrozen, cancellationToken);
        var completedTxns = await _dbContext.Transactions.CountAsync(t => t.Status == TransactionStatus.Completed, cancellationToken);

        return new LedgerReconciliationResponse(
            SystemTreasuryBalances: treasuryBalances,
            TotalPlatformCredits: totalCredits,
            TotalPlatformDebits: totalDebits,
            IsReconciled: isReconciled,
            TotalWalletsCount: totalWallets,
            FrozenWalletsCount: frozenWallets,
            TotalCompletedTransactions: completedTxns,
            ReconciledAtUtc: DateTime.UtcNow
        );
    }

    public async Task<PagedResult<WalletResponse>> GetWalletsAsync(
        int page = 1,
        int pageSize = 20,
        bool? isFrozen = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Wallets.AsNoTracking().AsQueryable();

        if (isFrozen.HasValue)
        {
            query = query.Where(w => w.IsFrozen == isFrozen.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var validPage = Math.Max(1, page);
        var validPageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(w => w.UpdatedAtUtc)
            .Skip((validPage - 1) * validPageSize)
            .Take(validPageSize)
            .Select(w => new WalletResponse(
                w.Id,
                w.UserId,
                w.Balance,
                w.Currency,
                w.UpdatedAtUtc,
                w.IsFrozen,
                w.FreezeReason
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<WalletResponse>.Create(items, validPage, validPageSize, totalCount);
    }
}
