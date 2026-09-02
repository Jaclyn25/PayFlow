namespace PayFlow.Application.DTOs;

public record DepositRequest(
    decimal Amount,
    string? Description
);

public record WithdrawRequest(
    decimal Amount,
    string? Description
);

public record WalletResponse(
    Guid Id,
    Guid UserId,
    decimal Balance,
    string Currency,
    DateTime UpdatedAtUtc
);
