using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class TransactionService : ITransactionService
{
    private readonly IPayFlowDbContext _dbContext;

    public TransactionService(IPayFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<TransactionResponse>> GetTransactionsAsync(
        Guid userId,
        TransactionFilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet == null)
        {
            throw new WalletNotFoundException($"User {userId}");
        }

        var query = _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.SenderWalletId == wallet.Id || t.ReceiverWalletId == wallet.Id);

        if (filterParams.Type.HasValue)
        {
            query = query.Where(t => t.Type == filterParams.Type.Value);
        }

        if (filterParams.Status.HasValue)
        {
            query = query.Where(t => t.Status == filterParams.Status.Value);
        }

        if (filterParams.FromDate.HasValue)
        {
            query = query.Where(t => t.CreatedAtUtc >= filterParams.FromDate.Value.ToUniversalTime());
        }

        if (filterParams.ToDate.HasValue)
        {
            query = query.Where(t => t.CreatedAtUtc <= filterParams.ToDate.Value.ToUniversalTime());
        }

        query = query.OrderByDescending(t => t.CreatedAtUtc);

        var page = filterParams.Page < 1 ? 1 : filterParams.Page;
        var pageSize = filterParams.PageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => filterParams.PageSize
        };

        var totalCount = await query.LongCountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => MapTransaction(t))
            .ToListAsync(cancellationToken);

        return PagedResult<TransactionResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<TransactionResponse> GetTransactionByIdAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet == null)
        {
            throw new WalletNotFoundException($"User {userId}");
        }

        var transaction = await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

        if (transaction == null)
        {
            throw new InvalidTransactionException($"Transaction with ID '{transactionId}' was not found.");
        }

        // Security check: ensure the transaction involves the requesting user's wallet
        if (transaction.SenderWalletId != wallet.Id && transaction.ReceiverWalletId != wallet.Id)
        {
            throw new UnauthorizedDomainException("You do not have permission to view this transaction.");
        }

        return MapTransaction(transaction);
    }

    private static TransactionResponse MapTransaction(Transaction t) => new(
        t.Id,
        t.Reference,
        t.Type.ToString(),
        t.Status.ToString(),
        t.Amount,
        t.Currency,
        t.SenderWalletId,
        t.ReceiverWalletId,
        t.Description,
        t.IdempotencyKey,
        t.CreatedAtUtc,
        t.CompletedAtUtc,
        t.FailureReason,
        t.ExchangeRate,
        t.TargetAmount,
        t.TargetCurrency
    );
}
