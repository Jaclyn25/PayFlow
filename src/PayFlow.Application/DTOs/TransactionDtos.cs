using PayFlow.Domain.Enums;

namespace PayFlow.Application.DTOs;

public record TransactionResponse(
    Guid Id,
    string Reference,
    string Type,
    string Status,
    decimal Amount,
    string Currency,
    Guid? SenderWalletId,
    Guid? ReceiverWalletId,
    string? Description,
    string? IdempotencyKey,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? FailureReason,
    decimal ExchangeRate = 1.0m,
    decimal? TargetAmount = null,
    string? TargetCurrency = null
);

public record TransactionFilterParams(
    int Page = 1,
    int PageSize = 20,
    TransactionType? Type = null,
    TransactionStatus? Status = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null
);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount,
    int TotalPages
)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, long totalCount)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagedResult<T>(items, page, pageSize, totalCount, totalPages == 0 ? 1 : totalPages);
    }
}
