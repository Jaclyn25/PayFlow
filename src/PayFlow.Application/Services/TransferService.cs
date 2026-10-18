using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class TransferService : ITransferService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly IAuditLogService? _auditLogService;
    private readonly IExchangeRateService? _exchangeRateService;

    public TransferService(
        IPayFlowDbContext dbContext,
        ICacheService cacheService,
        IAuditLogService? auditLogService = null,
        IExchangeRateService? exchangeRateService = null)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _auditLogService = auditLogService;
        _exchangeRateService = exchangeRateService;
    }

    public async Task<TransactionResponse> TransferAsync(
        Guid senderUserId,
        string idempotencyKey,
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidTransactionException("Idempotency-Key header is required for transfers.");
        }

        if (request.Amount <= 0)
        {
            throw new InvalidTransactionException("Transfer amount must be greater than zero.");
        }

        var normalizedReceiverEmail = request.ReceiverEmail.Trim().ToLowerInvariant();

        // 1. Database-level Idempotency Check
        var existingRecord = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == senderUserId && r.Key == idempotencyKey, cancellationToken);

        if (existingRecord != null)
        {
            var cachedResponse = JsonSerializer.Deserialize<TransactionResponse>(existingRecord.ResponseBody);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }
        }

        // 2. Validate Sender & Receiver
        var senderUser = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.Id == senderUserId, cancellationToken);

        if (senderUser == null || senderUser.Wallet == null)
        {
            throw new UserNotFoundException(senderUserId);
        }

        if (senderUser.Email.Equals(normalizedReceiverEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidTransactionException("Self-transfer is not allowed.");
        }

        var receiverUser = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.Email == normalizedReceiverEmail, cancellationToken);

        if (receiverUser == null || receiverUser.Wallet == null)
        {
            throw new UserNotFoundException(request.ReceiverEmail);
        }

        var senderWalletId = senderUser.Wallet.Id;
        var receiverWalletId = receiverUser.Wallet.Id;

        // Deterministic ordering to prevent deadlocks under concurrent transfers
        var (firstWalletId, secondWalletId) = senderWalletId.CompareTo(receiverWalletId) < 0
            ? (senderWalletId, receiverWalletId)
            : (receiverWalletId, senderWalletId);

        await using var dbTransaction = await _dbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            // Acquire locks in deterministic order if PostgreSQL provider is used
            var providerName = (_dbContext as DbContext)?.Database.ProviderName;
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

                senderWallet = senderWalletId == firstWalletId ? lockedFirst : lockedSecond;
                receiverWallet = receiverWalletId == firstWalletId ? lockedFirst : lockedSecond;
            }
            else
            {
                senderWallet = (await _dbContext.Wallets
                    .FirstOrDefaultAsync(w => w.Id == senderWalletId, cancellationToken))!;

                receiverWallet = (await _dbContext.Wallets
                    .FirstOrDefaultAsync(w => w.Id == receiverWalletId, cancellationToken))!;
            }

            if (senderWallet == null || receiverWallet == null)
            {
                throw new WalletNotFoundException("Wallet missing during transaction execution.");
            }

            // Invariant & Balance check
            if (senderWallet.Balance < request.Amount)
            {
                throw new InsufficientBalanceException(senderWallet.Balance, request.Amount);
            }

            var isCrossCurrency = !senderWallet.Currency.Equals(receiverWallet.Currency, StringComparison.OrdinalIgnoreCase);
            decimal rate = 1.0m;
            decimal targetAmount = request.Amount;

            if (isCrossCurrency)
            {
                if (_exchangeRateService != null)
                {
                    rate = await _exchangeRateService.GetExchangeRateAsync(
                        senderWallet.Currency,
                        receiverWallet.Currency,
                        cancellationToken);
                }

                targetAmount = Math.Round(request.Amount * rate, 2);
            }

            // Execute transfers
            senderWallet.Withdraw(request.Amount);
            receiverWallet.Deposit(targetAmount);

            // Create Transaction record
            var transaction = new Transaction
            {
                Reference = Transaction.GenerateReference(),
                Type = TransactionType.Transfer,
                Status = TransactionStatus.Completed,
                Amount = request.Amount,
                Currency = senderWallet.Currency,
                ExchangeRate = rate,
                TargetAmount = targetAmount,
                TargetCurrency = receiverWallet.Currency,
                SenderWalletId = senderWallet.Id,
                ReceiverWalletId = receiverWallet.Id,
                Description = request.Description ?? (isCrossCurrency 
                    ? $"Transfer to {receiverUser.Email} ({senderWallet.Currency} -> {receiverWallet.Currency} @ {rate})" 
                    : $"Transfer to {receiverUser.Email}"),
                IdempotencyKey = idempotencyKey,
                CreatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            };

            _dbContext.Transactions.Add(transaction);

            if (!isCrossCurrency)
            {
                // Single-Currency Double-Entry Ledger: Debit Sender, Credit Receiver
                var senderLedger = new LedgerEntry
                {
                    TransactionId = transaction.Id,
                    WalletId = senderWallet.Id,
                    EntryType = LedgerEntryType.Debit,
                    Amount = request.Amount,
                    Currency = senderWallet.Currency,
                    BalanceAfter = senderWallet.Balance,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var receiverLedger = new LedgerEntry
                {
                    TransactionId = transaction.Id,
                    WalletId = receiverWallet.Id,
                    EntryType = LedgerEntryType.Credit,
                    Amount = request.Amount,
                    Currency = receiverWallet.Currency,
                    BalanceAfter = receiverWallet.Balance,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.LedgerEntries.Add(senderLedger);
                _dbContext.LedgerEntries.Add(receiverLedger);
            }
            else
            {
                // Multi-Currency Double-Entry Ledger via System Treasury FX Clearing Account
                // 1) Source Currency: Debit Sender, Credit System Treasury FX Clearing
                var senderDebitLedger = new LedgerEntry
                {
                    TransactionId = transaction.Id,
                    WalletId = senderWallet.Id,
                    EntryType = LedgerEntryType.Debit,
                    Amount = request.Amount,
                    Currency = senderWallet.Currency,
                    BalanceAfter = senderWallet.Balance,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var systemFxCreditLedger = new LedgerEntry
                {
                    TransactionId = transaction.Id,
                    WalletId = SystemConstants.SystemWalletId,
                    EntryType = LedgerEntryType.Credit,
                    Amount = request.Amount,
                    Currency = senderWallet.Currency,
                    BalanceAfter = 0m,
                    CreatedAtUtc = DateTime.UtcNow
                };

                // 2) Destination Currency: Debit System Treasury FX Clearing, Credit Receiver
                var systemFxDebitLedger = new LedgerEntry
                {
                    TransactionId = transaction.Id,
                    WalletId = SystemConstants.SystemWalletId,
                    EntryType = LedgerEntryType.Debit,
                    Amount = targetAmount,
                    Currency = receiverWallet.Currency,
                    BalanceAfter = 0m,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var receiverCreditLedger = new LedgerEntry
                {
                    TransactionId = transaction.Id,
                    WalletId = receiverWallet.Id,
                    EntryType = LedgerEntryType.Credit,
                    Amount = targetAmount,
                    Currency = receiverWallet.Currency,
                    BalanceAfter = receiverWallet.Balance,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.LedgerEntries.Add(senderDebitLedger);
                _dbContext.LedgerEntries.Add(systemFxCreditLedger);
                _dbContext.LedgerEntries.Add(systemFxDebitLedger);
                _dbContext.LedgerEntries.Add(receiverCreditLedger);
            }

            // Create Outbox Message
            var transferEventPayload = JsonSerializer.Serialize(new
            {
                TransactionId = transaction.Id,
                Reference = transaction.Reference,
                SenderUserId = senderUser.Id,
                ReceiverUserId = receiverUser.Id,
                Amount = request.Amount,
                Currency = senderWallet.Currency,
                ExchangeRate = rate,
                TargetAmount = targetAmount,
                TargetCurrency = receiverWallet.Currency,
                OccurredAtUtc = DateTime.UtcNow
            });

            var outboxMessage = new OutboxMessage
            {
                Type = "TransferCompleted",
                Payload = transferEventPayload,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.OutboxMessages.Add(outboxMessage);

            var responseDto = MapTransaction(transaction);

            // Save Idempotency Record
            var idempotencyRecord = new IdempotencyRecord
            {
                UserId = senderUserId,
                Key = idempotencyKey,
                StatusCode = 200,
                ResponseBody = JsonSerializer.Serialize(responseDto),
                TransactionId = transaction.Id,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.IdempotencyRecords.Add(idempotencyRecord);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            if (_auditLogService != null)
            {
                await _auditLogService.LogAsync(
                    senderUserId,
                    "TransferMoney",
                    transaction.Id.ToString(),
                    true,
                    cancellationToken: cancellationToken);
            }

            // Invalidate Redis caches after transaction commit
            await _cacheService.RemoveAsync($"wallet:user:{senderUserId}", cancellationToken);
            await _cacheService.RemoveAsync($"wallet:user:{receiverUser.Id}", cancellationToken);

            return responseDto;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_IdempotencyRecords_UserId_Key", StringComparison.OrdinalIgnoreCase) == true
                                        || ex.InnerException?.Message.Contains("Idempotency", StringComparison.OrdinalIgnoreCase) == true)
        {
            await dbTransaction.RollbackAsync(cancellationToken);

            // Race condition: another request committed the idempotency key concurrently
            var record = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == senderUserId && r.Key == idempotencyKey, cancellationToken);

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
        t.TargetCurrency
    );
}
