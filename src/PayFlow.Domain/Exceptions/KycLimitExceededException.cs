using PayFlow.Domain.Enums;

namespace PayFlow.Domain.Exceptions;

public class KycLimitExceededException : Exception
{
    public KycTier CurrentTier { get; }
    public string LimitType { get; }
    public decimal LimitAmount { get; }
    public decimal CurrentUsage { get; }
    public string RequiredAction { get; }

    public KycLimitExceededException(
        string message,
        string limitType,
        decimal limitAmount,
        decimal currentUsage,
        string requiredAction = "Upgrade your KYC tier in account settings to increase transfer limits.",
        KycTier currentTier = KycTier.Tier1)
        : base(message)
    {
        LimitType = limitType;
        LimitAmount = limitAmount;
        CurrentUsage = currentUsage;
        RequiredAction = requiredAction;
        CurrentTier = currentTier;
    }
}
