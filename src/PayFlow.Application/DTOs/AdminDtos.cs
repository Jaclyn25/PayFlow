using PayFlow.Domain.Enums;

namespace PayFlow.Application.DTOs;

public record FreezeWalletRequest(
    Guid? WalletId = null,
    string? Reason = null
);

public record UnfreezeWalletRequest(
    Guid? WalletId = null
);

public record WalletFreezeResponse(
    Guid WalletId,
    Guid UserId,
    string UserEmail,
    bool IsFrozen,
    string? FreezeReason,
    DateTime? FrozenAtUtc
);

public record AdminUserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string KycTier,
    DateTime? KycVerifiedAtUtc,
    bool TwoFactorEnabled,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? WalletId,
    decimal WalletBalance,
    string WalletCurrency,
    bool IsWalletFrozen,
    bool EmailConfirmed = false
);

public record AdminUserFilterParams(
    string? Search = null,
    string? Role = null,
    string? KycTier = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20
);

public record LedgerReconciliationResponse(
    IReadOnlyDictionary<string, decimal> SystemTreasuryBalances,
    decimal TotalPlatformCredits,
    decimal TotalPlatformDebits,
    bool IsReconciled,
    int TotalWalletsCount,
    int FrozenWalletsCount,
    int TotalCompletedTransactions,
    DateTime ReconciledAtUtc
);

public record UpdateUserRoleRequest(
    UserRole Role
);
