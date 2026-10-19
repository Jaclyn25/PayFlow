using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IStatementService
{
    Task<StatementSummaryDto> GetStatementDataAsync(
        Guid userId,
        Guid? walletId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default);

    Task<byte[]> GenerateCsvStatementAsync(
        Guid userId,
        Guid? walletId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default);

    Task<byte[]> GeneratePdfStatementAsync(
        Guid userId,
        Guid? walletId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default);
}
