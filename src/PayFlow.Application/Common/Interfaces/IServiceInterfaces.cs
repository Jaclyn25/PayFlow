using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default);
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<MessageResponse> VerifyEmailAsync(string token, CancellationToken cancellationToken = default);
    Task<MessageResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<MessageResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
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
    Task<TransactionResponse> ReverseTransactionAsync(Guid userId, Guid transactionId, ReverseTransactionRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default);
}
