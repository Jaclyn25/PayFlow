namespace PayFlow.Application.DTOs;

public record TransferRequest(
    string ReceiverEmail,
    decimal Amount,
    string? Description
);
