using PayFlow.Application.DTOs;
using PayFlow.Domain.Enums;

namespace PayFlow.Application.Common.Interfaces;

public interface IAdminService
{
    Task<WalletFreezeResponse> FreezeWalletAsync(Guid walletId, string? reason, CancellationToken cancellationToken = default);
    Task<WalletFreezeResponse> UnfreezeWalletAsync(Guid walletId, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminUserDto>> GetUsersAsync(AdminUserFilterParams filterParams, CancellationToken cancellationToken = default);
    Task<AdminUserDto> UpdateUserRoleAsync(Guid userId, UserRole role, CancellationToken cancellationToken = default);
    Task<LedgerReconciliationResponse> GetLedgerReconciliationAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<WalletResponse>> GetWalletsAsync(int page = 1, int pageSize = 20, bool? isFrozen = null, CancellationToken cancellationToken = default);
}
