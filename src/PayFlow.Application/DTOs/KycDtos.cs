using PayFlow.Domain.Enums;

namespace PayFlow.Application.DTOs;

public record KycTierLimitsResponse(
    Guid UserId,
    string KycTier,
    decimal MaxSingleTransaction,
    decimal DailyLimit,
    decimal CurrentDailyUsage,
    decimal RemainingDailyLimit,
    decimal MonthlyLimit,
    decimal CurrentMonthlyUsage,
    decimal RemainingMonthlyLimit,
    string Currency
);

public record UpdateKycTierRequest(
    KycTier Tier
);
