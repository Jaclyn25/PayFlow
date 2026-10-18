using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class TransactionService : ITransactionService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly ICacheService? _cacheService;
    private readonly IAuditLogService? _auditLogService;

    public TransactionService(
        IPayFlowDbContext dbContext,
        ICacheService? cacheService = null,
        IAuditLogService? auditLogService = null)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _auditLogService = auditLogService;
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

    public async Task<TransactionResponse> ReverseTransactionAsync(
        Guid userId,
        Guid transactionId,
        ReverseTransactionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            throw new InvalidTransactionException("Reversal reason is required.");
        }

        // 1. Database-level Idempotency Check if key provided
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingRecord = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == idempotencyKey, cancellationToken);

            if (existingRecord != null)
            {
                var cachedResponse = JsonSerializer.Deserialize<TransactionResponse>(existingRecord.ResponseBody);
                if (cachedResponse != null)
                {
                    return cachedResponse;
                }
            }
        }

        await using var dbTransaction = await _dbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            var providerName = (_dbContext as DbContext)?.Database.ProviderName;
            Transaction? originalTxn;

            if (providerName != null && providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                originalTxn = await _dbContext.Transactions
                    .FromSqlInterpolated($"SELECT * FROM \"Transactions\" WHERE \"Id\" = {transactionId} FOR UPDATE")
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                originalTxn = await _dbContext.Transactions
                    .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);
            }

            if (originalTxn == null)
            {
                throw new InvalidTransactionException($"Transaction with ID '{transactionId}' was not found.");
            }

            // Invariants: can only reverse completed non-reversal transactions once
            if (originalTxn.Status == TransactionStatus.Refunded || originalTxn.ReversalTransactionId.HasValue)
            {
                throw new InvalidTransactionException($"Transaction '{originalTxn.Reference}' has already been reversed.");
            }

            if (originalTxn.Status != TransactionStatus.Completed)
            {
                throw new InvalidTransactionException($"Only completed transactions can be reversed. Current status: {originalTxn.Status}.");
            }

            if (originalTxn.Type == TransactionType.Reversal)
            {
                throw new InvalidTransactionException("A reversal transaction cannot be reversed.");
            }

            // Authorization check
            var userWallet = await _dbContext.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

            if (userWallet == null)
            {
                throw new WalletNotFoundException($"User {userId}");
            }

            if (originalTxn.SenderWalletId != userWallet.Id && originalTxn.ReceiverWalletId != userWallet.Id)
            {
                throw new UnauthorizedDomainException("You do not have permission to reverse this transaction.");
            }

            Transaction reversalTxn;

            if (originalTxn.Type == TransactionType.Transfer)
            {
                var originalReceiverId = originalTxn.ReceiverWalletId!.Value;
                var originalSenderId = originalTxn.SenderWalletId!.Value;

                var (firstWalletId, secondWalletId) = originalReceiverId.CompareTo(originalSenderId) < 0
                    ? (originalReceiverId, originalSenderId)
                    : (originalSenderId, originalReceiverId);

                Wallet senderWallet;
                Wallet receiverWallet;

                if (providerName != null && providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                {
                    var lockedFirst = await _dbContext.Wallets
                        .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {firstWalletId} FOR UPDATE")
                        .FirstAsync(cancellationToken);

                    var lockedSecond = await _dbContext.Wallets
                        .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {secondWalletId} FOR UPDATE")
                        .FirstAsync(cancellationToken);

                    receiverWallet = originalReceiverId == firstWalletId ? lockedFirst : lockedSecond;
                    senderWallet = originalSenderId == firstWalletId ? lockedFirst : lockedSecond;
                }
                else
                {
                    receiverWallet = (await _dbContext.Wallets
                        .FirstOrDefaultAsync(w => w.Id == originalReceiverId, cancellationToken))!;

                    senderWallet = (await _dbContext.Wallets
                        .FirstOrDefaultAsync(w => w.Id == originalSenderId, cancellationToken))!;
                }

                if (senderWallet == null || receiverWallet == null)
                {
                    throw new WalletNotFoundException("Wallet missing during transaction reversal.");
                }

                var isCrossCurrency = originalTxn.TargetCurrency != null &&
                    !originalTxn.Currency.Equals(originalTxn.TargetCurrency, StringComparison.OrdinalIgnoreCase);

                var debitAmount = isCrossCurrency ? originalTxn.TargetAmount!.Value : originalTxn.Amount;
                var creditAmount = originalTxn.Amount;

                // Receiver must have sufficient funds to return
                if (receiverWallet.Balance < debitAmount)
                {
                    throw new InsufficientBalanceException(receiverWallet.Balance, debitAmount);
                }

                // Balance mutation
                receiverWallet.Withdraw(debitAmount);
                senderWallet.Deposit(creditAmount);

                reversalTxn = new Transaction
                {
                    Reference = Transaction.GenerateReference(),
                    Type = TransactionType.Reversal,
                    Status = TransactionStatus.Completed,
                    Amount = originalTxn.Amount,
                    Currency = originalTxn.Currency,
                    ExchangeRate = originalTxn.ExchangeRate,
                    TargetAmount = originalTxn.TargetAmount,
                    TargetCurrency = originalTxn.TargetCurrency,
                    SenderWalletId = receiverWallet.Id,
                    ReceiverWalletId = senderWallet.Id,
                    OriginalTransactionId = originalTxn.Id,
                    ReversalReason = request.Reason,
                    Description = $"Reversal of {originalTxn.Reference}: {request.Reason}",
                    IdempotencyKey = idempotencyKey,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                };

                _dbContext.Transactions.Add(reversalTxn);

                originalTxn.Status = TransactionStatus.Refunded;
                originalTxn.ReversalTransactionId = reversalTxn.Id;
                originalTxn.ReversalReason = request.Reason;

                if (!isCrossCurrency)
                {
                    var receiverDebitLedger = new LedgerEntry
                    {
                        TransactionId = reversalTxn.Id,
                        WalletId = receiverWallet.Id,
                        EntryType = LedgerEntryType.Debit,
                        Amount = originalTxn.Amount,
                        Currency = originalTxn.Currency,
                        BalanceAfter = receiverWallet.Balance,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    var senderCreditLedger = new LedgerEntry
                    {
                        TransactionId = reversalTxn.Id,
                        WalletId = senderWallet.Id,
                        EntryType = LedgerEntryType.Credit,
                        Amount = originalTxn.Amount,
                        Currency = originalTxn.Currency,
                        BalanceAfter = senderWallet.Balance,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    _dbContext.LedgerEntries.Add(receiverDebitLedger);
                    _dbContext.LedgerEntries.Add(senderCreditLedger);
                }
                else
                {
                    // Target currency leg: Debit receiver, Credit System FX Clearing
                    var receiverDebitLedger = new LedgerEntry
                    {
                        TransactionId = reversalTxn.Id,
                        WalletId = receiverWallet.Id,
                        EntryType = LedgerEntryType.Debit,
                        Amount = debitAmount,
                        Currency = originalTxn.TargetCurrency!,
                        BalanceAfter = receiverWallet.Balance,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    var systemFxCreditLedger = new LedgerEntry
                    {
                        TransactionId = reversalTxn.Id,
                        WalletId = SystemConstants.SystemWalletId,
                        EntryType = LedgerEntryType.Credit,
                        Amount = debitAmount,
                        Currency = originalTxn.TargetCurrency!,
                        BalanceAfter = 0m,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    // Source currency leg: Debit System FX Clearing, Credit sender
                    var systemFxDebitLedger = new LedgerEntry
                    {
                        TransactionId = reversalTxn.Id,
                        WalletId = SystemConstants.SystemWalletId,
                        EntryType = LedgerEntryType.Debit,
                        Amount = creditAmount,
                        Currency = originalTxn.Currency,
                        BalanceAfter = 0m,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    var senderCreditLedger = new LedgerEntry
                    {
                        TransactionId = reversalTxn.Id,
                        WalletId = senderWallet.Id,
                        EntryType = LedgerEntryType.Credit,
                        Amount = creditAmount,
                        Currency = originalTxn.Currency,
                        BalanceAfter = senderWallet.Balance,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    _dbContext.LedgerEntries.Add(receiverDebitLedger);
                    _dbContext.LedgerEntries.Add(systemFxCreditLedger);
                    _dbContext.LedgerEntries.Add(systemFxDebitLedger);
                    _dbContext.LedgerEntries.Add(senderCreditLedger);
                }
            }
            else if (originalTxn.Type == TransactionType.Deposit)
            {
                var targetWalletId = originalTxn.ReceiverWalletId!.Value;
                Wallet targetWallet;
                if (providerName != null && providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                {
                    targetWallet = await _dbContext.Wallets
                        .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {targetWalletId} FOR UPDATE")
                        .FirstAsync(cancellationToken);
                }
                else
                {
                    targetWallet = (await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == targetWalletId, cancellationToken))!;
                }

                if (targetWallet.Balance < originalTxn.Amount)
                {
                    throw new InsufficientBalanceException(targetWallet.Balance, originalTxn.Amount);
                }

                targetWallet.Withdraw(originalTxn.Amount);

                reversalTxn = new Transaction
                {
                    Reference = Transaction.GenerateReference(),
                    Type = TransactionType.Reversal,
                    Status = TransactionStatus.Completed,
                    Amount = originalTxn.Amount,
                    Currency = originalTxn.Currency,
                    SenderWalletId = targetWallet.Id,
                    ReceiverWalletId = SystemConstants.SystemWalletId,
                    OriginalTransactionId = originalTxn.Id,
                    ReversalReason = request.Reason,
                    Description = $"Reversal of deposit {originalTxn.Reference}: {request.Reason}",
                    IdempotencyKey = idempotencyKey,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                };

                _dbContext.Transactions.Add(reversalTxn);
                originalTxn.Status = TransactionStatus.Refunded;
                originalTxn.ReversalTransactionId = reversalTxn.Id;
                originalTxn.ReversalReason = request.Reason;

                var debitEntry = new LedgerEntry
                {
                    TransactionId = reversalTxn.Id,
                    WalletId = targetWallet.Id,
                    EntryType = LedgerEntryType.Debit,
                    Amount = originalTxn.Amount,
                    Currency = originalTxn.Currency,
                    BalanceAfter = targetWallet.Balance,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var creditEntry = new LedgerEntry
                {
                    TransactionId = reversalTxn.Id,
                    WalletId = SystemConstants.SystemWalletId,
                    EntryType = LedgerEntryType.Credit,
                    Amount = originalTxn.Amount,
                    Currency = originalTxn.Currency,
                    BalanceAfter = 0m,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.LedgerEntries.Add(debitEntry);
                _dbContext.LedgerEntries.Add(creditEntry);
            }
            else if (originalTxn.Type == TransactionType.Withdrawal)
            {
                var targetWalletId = originalTxn.SenderWalletId!.Value;
                Wallet targetWallet;
                if (providerName != null && providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                {
                    targetWallet = await _dbContext.Wallets
                        .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {targetWalletId} FOR UPDATE")
                        .FirstAsync(cancellationToken);
                }
                else
                {
                    targetWallet = (await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == targetWalletId, cancellationToken))!;
                }

                targetWallet.Deposit(originalTxn.Amount);

                reversalTxn = new Transaction
                {
                    Reference = Transaction.GenerateReference(),
                    Type = TransactionType.Reversal,
                    Status = TransactionStatus.Completed,
                    Amount = originalTxn.Amount,
                    Currency = originalTxn.Currency,
                    SenderWalletId = SystemConstants.SystemWalletId,
                    ReceiverWalletId = targetWallet.Id,
                    OriginalTransactionId = originalTxn.Id,
                    ReversalReason = request.Reason,
                    Description = $"Reversal of withdrawal {originalTxn.Reference}: {request.Reason}",
                    IdempotencyKey = idempotencyKey,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                };

                _dbContext.Transactions.Add(reversalTxn);
                originalTxn.Status = TransactionStatus.Refunded;
                originalTxn.ReversalTransactionId = reversalTxn.Id;
                originalTxn.ReversalReason = request.Reason;

                var debitEntry = new LedgerEntry
                {
                    TransactionId = reversalTxn.Id,
                    WalletId = SystemConstants.SystemWalletId,
                    EntryType = LedgerEntryType.Debit,
                    Amount = originalTxn.Amount,
                    Currency = originalTxn.Currency,
                    BalanceAfter = 0m,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var creditEntry = new LedgerEntry
                {
                    TransactionId = reversalTxn.Id,
                    WalletId = targetWallet.Id,
                    EntryType = LedgerEntryType.Credit,
                    Amount = originalTxn.Amount,
                    Currency = originalTxn.Currency,
                    BalanceAfter = targetWallet.Balance,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.LedgerEntries.Add(debitEntry);
                _dbContext.LedgerEntries.Add(creditEntry);
            }
            else
            {
                throw new InvalidTransactionException($"Unsupported transaction type for reversal: {originalTxn.Type}.");
            }

            // Create Outbox message
            var reversalPayload = JsonSerializer.Serialize(new
            {
                ReversalTransactionId = reversalTxn.Id,
                OriginalTransactionId = originalTxn.Id,
                Reference = reversalTxn.Reference,
                OriginalReference = originalTxn.Reference,
                Amount = reversalTxn.Amount,
                Currency = reversalTxn.Currency,
                Reason = request.Reason,
                OccurredAtUtc = DateTime.UtcNow
            });

            _dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Type = "TransactionReversed",
                Payload = reversalPayload,
                CreatedAtUtc = DateTime.UtcNow
            });

            var responseDto = MapTransaction(reversalTxn);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var idempotencyRecord = new IdempotencyRecord
                {
                    UserId = userId,
                    Key = idempotencyKey,
                    StatusCode = 200,
                    ResponseBody = JsonSerializer.Serialize(responseDto),
                    TransactionId = reversalTxn.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.IdempotencyRecords.Add(idempotencyRecord);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            if (_auditLogService != null)
            {
                await _auditLogService.LogAsync(
                    userId,
                    "ReverseTransaction",
                    reversalTxn.Id.ToString(),
                    true,
                    cancellationToken: cancellationToken);
            }

            if (_cacheService != null)
            {
                if (originalTxn.SenderWalletId.HasValue && originalTxn.SenderWalletId != SystemConstants.SystemWalletId)
                {
                    var sw = await _dbContext.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == originalTxn.SenderWalletId, cancellationToken);
                    if (sw != null)
                    {
                        await _cacheService.RemoveAsync($"wallet:user:{sw.UserId}", cancellationToken);
                    }
                }

                if (originalTxn.ReceiverWalletId.HasValue && originalTxn.ReceiverWalletId != SystemConstants.SystemWalletId)
                {
                    var rw = await _dbContext.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == originalTxn.ReceiverWalletId, cancellationToken);
                    if (rw != null)
                    {
                        await _cacheService.RemoveAsync($"wallet:user:{rw.UserId}", cancellationToken);
                    }
                }
            }

            return responseDto;
        }
        catch (DbUpdateException ex) when (idempotencyKey != null &&
            (ex.InnerException?.Message.Contains("IX_IdempotencyRecords_UserId_Key", StringComparison.OrdinalIgnoreCase) == true
             || ex.InnerException?.Message.Contains("Idempotency", StringComparison.OrdinalIgnoreCase) == true))
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            var record = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == idempotencyKey, cancellationToken);

            if (record != null)
            {
                var cachedResponse = JsonSerializer.Deserialize<TransactionResponse>(record.ResponseBody);
                if (cachedResponse != null)
                {
                    return cachedResponse;
                }
            }

            throw new DuplicateIdempotencyException(idempotencyKey);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
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
        t.TargetCurrency,
        t.OriginalTransactionId,
        t.ReversalTransactionId,
        t.ReversalReason
    );
}
