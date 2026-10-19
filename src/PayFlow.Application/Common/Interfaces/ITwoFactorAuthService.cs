using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;

namespace PayFlow.Application.Common.Interfaces;

public interface ITwoFactorAuthService
{
    Task<TwoFactorSetupResponse> SetupAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> EnableAsync(Guid userId, string code, CancellationToken cancellationToken = default);
    Task<bool> DisableAsync(Guid userId, string code, CancellationToken cancellationToken = default);
    Task<TwoFactorStatusResponse> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AuthResponse> VerifyTwoFactorLoginAsync(VerifyTwoFactorRequest request, CancellationToken cancellationToken = default);
    Task ValidateStepUpTransferAsync(User user, decimal amount, string? twoFactorCode, CancellationToken cancellationToken = default);
    bool ValidateTotp(string secret, string code, int toleranceSteps = 1, string? replayKeyPrefix = null);
    string GenerateTotp(string secret, DateTime? timestampUtc = null);
}
