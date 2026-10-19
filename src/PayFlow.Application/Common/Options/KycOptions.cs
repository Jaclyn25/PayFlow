using PayFlow.Domain.Enums;

namespace PayFlow.Application.Common.Options;

public class KycTierLimitConfig
{
    public decimal MaxSingleTransaction { get; set; }
    public decimal DailyLimit { get; set; }
    public decimal MonthlyLimit { get; set; }
}

public class KycOptions
{
    public const string SectionName = "KycSettings";

    public bool Enabled { get; set; } = true;

    public Dictionary<string, KycTierLimitConfig> TierLimits { get; set; } = new()
    {
        [KycTier.Unverified.ToString()] = new KycTierLimitConfig
        {
            MaxSingleTransaction = 1000m,
            DailyLimit = 2500m,
            MonthlyLimit = 10000m
        },
        [KycTier.Tier1.ToString()] = new KycTierLimitConfig
        {
            MaxSingleTransaction = 5000m,
            DailyLimit = 15000m,
            MonthlyLimit = 50000m
        },
        [KycTier.Tier2.ToString()] = new KycTierLimitConfig
        {
            MaxSingleTransaction = 50000m,
            DailyLimit = 150000m,
            MonthlyLimit = 500000m
        },
        [KycTier.Tier3.ToString()] = new KycTierLimitConfig
        {
            MaxSingleTransaction = 500000m,
            DailyLimit = 2000000m,
            MonthlyLimit = 10000000m
        }
    };
}
