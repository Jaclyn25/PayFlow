using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;

namespace PayFlow.Application.Common.Interfaces;

public interface IKycLimitService
{
    Task ValidateTransferLimitsAsync(
        User user,
        Guid senderWalletId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);

    Task<KycTierLimitsResponse> GetUserLimitsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserProfileResponse> UpdateUserTierAsync(
        Guid userId,
        KycTier tier,
        CancellationToken cancellationToken = default);
}
