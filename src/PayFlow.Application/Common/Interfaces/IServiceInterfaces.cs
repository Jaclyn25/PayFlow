using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default);
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IWalletService
{
    Task<WalletResponse> GetWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<TransactionResponse> DepositAsync(Guid userId, DepositRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResponse> WithdrawAsync(Guid userId, WithdrawRequest request, CancellationToken cancellationToken = default);
}

public interface ITransferService
{
    Task<TransactionResponse> TransferAsync(Guid senderUserId, string idempotencyKey, TransferRequest request, CancellationToken cancellationToken = default);
}

public interface ITransactionService
{
    Task<PagedResult<TransactionResponse>> GetTransactionsAsync(Guid userId, TransactionFilterParams filterParams, CancellationToken cancellationToken = default);
    Task<TransactionResponse> GetTransactionByIdAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default);
}
