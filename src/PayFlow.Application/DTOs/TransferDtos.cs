namespace PayFlow.Application.DTOs;

public record TransferRequest(
    string ReceiverEmail,
    decimal Amount,
    string? Description,
    string? TwoFactorCode = null
);

