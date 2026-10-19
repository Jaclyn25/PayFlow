using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class WalletService : IWalletService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly ICacheService _cacheService;

    public WalletService(
        IPayFlowDbContext dbContext,
        ICacheService cacheService)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
    }

    public async Task<WalletResponse> GetWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"wallet:user:{userId}";
        var cachedWallet = await _cacheService.GetAsync<WalletResponse>(cacheKey, cancellationToken);
        if (cachedWallet != null)
        {
            return cachedWallet;
        }

        var wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet == null)
        {
            throw new WalletNotFoundException($"User {userId}");
        }

        var response = new WalletResponse(
            wallet.Id,
            wallet.UserId,
            wallet.Balance,
            wallet.Currency,
            wallet.UpdatedAtUtc
        );

        await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5), cancellationToken);

        return response;
    }

    public async Task<TransactionResponse> DepositAsync(Guid userId, DepositRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidTransactionException("Deposit amount must be greater than zero.");
        }

        await using var dbTransaction = await _dbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            var wallet = await _dbContext.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

            if (wallet == null)
            {
                throw new WalletNotFoundException($"User {userId}");
            }

            wallet.Deposit(request.Amount);

            var transaction = new Transaction
            {
                Reference = Transaction.GenerateReference(),
                Type = TransactionType.Deposit,
                Status = TransactionStatus.Completed,
                Amount = request.Amount,
                Currency = wallet.Currency,
                SenderWalletId = SystemConstants.SystemWalletId,
                ReceiverWalletId = wallet.Id,
                Description = request.Description ?? "Demo Deposit",
                CreatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            };

            _dbContext.Transactions.Add(transaction);

            // Double-Entry Accounting Ledger Entries: Debit System Treasury, Credit User Wallet
            var systemDebitLedger = new LedgerEntry
            {
                TransactionId = transaction.Id,
                WalletId = SystemConstants.SystemWalletId,
                EntryType = LedgerEntryType.Debit,
                Amount = request.Amount,
                Currency = wallet.Currency,
                BalanceAfter = 0m,
                CreatedAtUtc = DateTime.UtcNow
            };

            var userCreditLedger = new LedgerEntry
            {
                TransactionId = transaction.Id,
                WalletId = wallet.Id,
                EntryType = LedgerEntryType.Credit,
                Amount = request.Amount,
                Currency = wallet.Currency,
                BalanceAfter = wallet.Balance,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.LedgerEntries.Add(systemDebitLedger);
            _dbContext.LedgerEntries.Add(userCreditLedger);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            await _cacheService.RemoveAsync($"wallet:user:{userId}", cancellationToken);

            return MapTransaction(transaction);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TransactionResponse> WithdrawAsync(Guid userId, WithdrawRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidTransactionException("Withdrawal amount must be greater than zero.");
        }

        await using var dbTransaction = await _dbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            var wallet = await _dbContext.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

            if (wallet == null)
            {
                throw new WalletNotFoundException($"User {userId}");
            }

            wallet.Withdraw(request.Amount);

            var transaction = new Transaction
            {
                Reference = Transaction.GenerateReference(),
                Type = TransactionType.Withdrawal,
                Status = TransactionStatus.Completed,
                Amount = request.Amount,
                Currency = wallet.Currency,
                SenderWalletId = wallet.Id,
                ReceiverWalletId = SystemConstants.SystemWalletId,
                Description = request.Description ?? "Demo Withdrawal",
                CreatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            };

            _dbContext.Transactions.Add(transaction);

            // Double-Entry Accounting Ledger Entries: Debit User Wallet, Credit System Treasury
            var userDebitLedger = new LedgerEntry
            {
                TransactionId = transaction.Id,
                WalletId = wallet.Id,
                EntryType = LedgerEntryType.Debit,
                Amount = request.Amount,
                Currency = wallet.Currency,
                BalanceAfter = wallet.Balance,
                CreatedAtUtc = DateTime.UtcNow
            };

            var systemCreditLedger = new LedgerEntry
            {
                TransactionId = transaction.Id,
                WalletId = SystemConstants.SystemWalletId,
                EntryType = LedgerEntryType.Credit,
                Amount = request.Amount,
                Currency = wallet.Currency,
                BalanceAfter = 0m,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.LedgerEntries.Add(userDebitLedger);
            _dbContext.LedgerEntries.Add(systemCreditLedger);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            await _cacheService.RemoveAsync($"wallet:user:{userId}", cancellationToken);

            return MapTransaction(transaction);
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
        t.ReversalReason,
        t.FeeAmount,
        t.GrossAmount,
        t.NetAmount
    );
}
