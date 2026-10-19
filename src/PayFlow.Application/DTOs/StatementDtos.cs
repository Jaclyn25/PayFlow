namespace PayFlow.Application.DTOs;

public record StatementEntryDto(
    DateTime Date,
    Guid TransactionId,
    string Reference,
    string Type,
    string Description,
    decimal? Debit,
    decimal? Credit,
    decimal BalanceAfter,
    string Currency
);

public record StatementSummaryDto(
    Guid WalletId,
    Guid UserId,
    string UserName,
    string UserEmail,
    string Currency,
    DateTime FromDate,
    DateTime ToDate,
    DateTime GeneratedAt,
    decimal OpeningBalance,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal NetMovement,
    decimal ClosingBalance,
    int TransactionCount,
    string VerificationHash,
    IReadOnlyList<StatementEntryDto> Entries
);

public record StatementQueryParameters(
    DateTime? From = null,
    DateTime? To = null,
    string? Format = "pdf"
);
