using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.Common.Options;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class KycLimitService : IKycLimitService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly KycOptions _options;

    public KycLimitService(IPayFlowDbContext dbContext, IOptions<KycOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task ValidateTransferLimitsAsync(
        User user,
        Guid senderWalletId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var tierKey = user.KycTier.ToString();
        if (!_options.TierLimits.TryGetValue(tierKey, out var limits))
        {
            limits = _options.TierLimits.TryGetValue(KycTier.Tier1.ToString(), out var defaultLimits)
                ? defaultLimits
                : new KycTierLimitConfig { MaxSingleTransaction = 5000m, DailyLimit = 15000m, MonthlyLimit = 50000m };
        }

        // 1. Single transaction limit check
        if (amount > limits.MaxSingleTransaction)
        {
            throw new KycLimitExceededException(
                $"Transfer amount {amount:N2} {currency} exceeds maximum single transaction limit of {limits.MaxSingleTransaction:N2} {currency} for KYC tier '{user.KycTier}'.",
                limitType: "SingleTransaction",
                limitAmount: limits.MaxSingleTransaction,
                currentUsage: amount,
                requiredAction: GetUpgradePrompt(user.KycTier),
                currentTier: user.KycTier);
        }

        // 2. Rolling 24-hour daily limit check
        var now = DateTime.UtcNow;
        var last24Hours = now.AddHours(-24);

        var dailyUsage = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.SenderWalletId == senderWalletId &&
                        t.Type == TransactionType.Transfer &&
                        (t.Status == TransactionStatus.Completed || t.Status == TransactionStatus.Pending) &&
                        t.CreatedAtUtc >= last24Hours)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0.00m;

        if (dailyUsage + amount > limits.DailyLimit)
        {
            throw new KycLimitExceededException(
                $"Transfer amount {amount:N2} {currency} exceeds rolling 24-hour limit of {limits.DailyLimit:N2} {currency} for KYC tier '{user.KycTier}'. Current 24h usage: {dailyUsage:N2} {currency}.",
                limitType: "DailyLimit24h",
                limitAmount: limits.DailyLimit,
                currentUsage: dailyUsage,
                requiredAction: GetUpgradePrompt(user.KycTier),
                currentTier: user.KycTier);
        }

        // 3. Rolling 30-day monthly limit check
        var last30Days = now.AddDays(-30);

        var monthlyUsage = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.SenderWalletId == senderWalletId &&
                        t.Type == TransactionType.Transfer &&
                        (t.Status == TransactionStatus.Completed || t.Status == TransactionStatus.Pending) &&
                        t.CreatedAtUtc >= last30Days)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0.00m;

        if (monthlyUsage + amount > limits.MonthlyLimit)
        {
            throw new KycLimitExceededException(
                $"Transfer amount {amount:N2} {currency} exceeds rolling 30-day limit of {limits.MonthlyLimit:N2} {currency} for KYC tier '{user.KycTier}'. Current 30d usage: {monthlyUsage:N2} {currency}.",
                limitType: "MonthlyLimit30d",
                limitAmount: limits.MonthlyLimit,
                currentUsage: monthlyUsage,
                requiredAction: GetUpgradePrompt(user.KycTier),
                currentTier: user.KycTier);
        }
    }

    public async Task<KycTierLimitsResponse> GetUserLimitsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        var tierKey = user.KycTier.ToString();
        if (!_options.TierLimits.TryGetValue(tierKey, out var limits))
        {
            limits = _options.TierLimits[KycTier.Tier1.ToString()];
        }

        var currency = user.Wallet?.Currency ?? "EGP";
        var senderWalletId = user.Wallet?.Id ?? Guid.Empty;

        var now = DateTime.UtcNow;
        var last24Hours = now.AddHours(-24);
        var last30Days = now.AddDays(-30);

        var dailyUsage = 0.00m;
        var monthlyUsage = 0.00m;

        if (senderWalletId != Guid.Empty)
        {
            dailyUsage = await _dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.SenderWalletId == senderWalletId &&
                            t.Type == TransactionType.Transfer &&
                            (t.Status == TransactionStatus.Completed || t.Status == TransactionStatus.Pending) &&
                            t.CreatedAtUtc >= last24Hours)
                .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0.00m;

            monthlyUsage = await _dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.SenderWalletId == senderWalletId &&
                            t.Type == TransactionType.Transfer &&
                            (t.Status == TransactionStatus.Completed || t.Status == TransactionStatus.Pending) &&
                            t.CreatedAtUtc >= last30Days)
                .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0.00m;
        }

        var remainingDaily = Math.Max(0m, limits.DailyLimit - dailyUsage);
        var remainingMonthly = Math.Max(0m, limits.MonthlyLimit - monthlyUsage);

        return new KycTierLimitsResponse(
            UserId: user.Id,
            KycTier: user.KycTier.ToString(),
            MaxSingleTransaction: limits.MaxSingleTransaction,
            DailyLimit: limits.DailyLimit,
            CurrentDailyUsage: dailyUsage,
            RemainingDailyLimit: remainingDaily,
            MonthlyLimit: limits.MonthlyLimit,
            CurrentMonthlyUsage: monthlyUsage,
            RemainingMonthlyLimit: remainingMonthly,
            Currency: currency
        );
    }

    public async Task<UserProfileResponse> UpdateUserTierAsync(
        Guid userId,
        KycTier tier,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new UserNotFoundException(userId);
        }

        user.KycTier = tier;
        user.KycVerifiedAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UserProfileResponse(
            Id: user.Id,
            FirstName: user.FirstName,
            LastName: user.LastName,
            Email: user.Email,
            IsActive: user.IsActive,
            CreatedAtUtc: user.CreatedAtUtc,
            KycTier: user.KycTier.ToString()
        );
    }

    private static string GetUpgradePrompt(KycTier tier) => tier switch
    {
        KycTier.Unverified => "Complete Tier 1 basic verification in account settings to unlock higher limits.",
        KycTier.Tier1 => "Upgrade to Tier 2 by uploading your national ID or passport to increase transfer limits.",
        KycTier.Tier2 => "Contact support or submit business registration documents to upgrade to Tier 3 Enterprise limits.",
        _ => "You have reached the highest tier limits."
    };
}
