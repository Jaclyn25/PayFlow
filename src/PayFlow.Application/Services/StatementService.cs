using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Enums;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Application.Services;

public class StatementService : IStatementService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly IStatementReportGenerator _reportGenerator;

    public StatementService(
        IPayFlowDbContext dbContext,
        IStatementReportGenerator reportGenerator)
    {
        _dbContext = dbContext;
        _reportGenerator = reportGenerator;
    }

    public async Task<StatementSummaryDto> GetStatementDataAsync(
        Guid userId,
        Guid? walletId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        Wallet? wallet;

        if (walletId.HasValue)
        {
            wallet = await _dbContext.Wallets
                .AsNoTracking()
                .Include(w => w.User)
                .FirstOrDefaultAsync(w => w.Id == walletId.Value, cancellationToken);

            if (wallet == null)
            {
                throw new WalletNotFoundException($"Wallet ID '{walletId.Value}' was not found.");
            }

            if (wallet.UserId != userId)
            {
                throw new UnauthorizedDomainException("You do not have permission to view statements for this wallet.");
            }
        }
        else
        {
            wallet = await _dbContext.Wallets
                .AsNoTracking()
                .Include(w => w.User)
                .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

            if (wallet == null)
            {
                throw new WalletNotFoundException($"User {userId}");
            }
        }

        var toUtc = toDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var fromUtc = fromDate?.ToUniversalTime() ?? toUtc.AddDays(-30);

        if (fromUtc > toUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        // 1. Calculate Opening Balance before fromUtc
        var lastEntryBeforeFrom = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(l => l.WalletId == wallet.Id && l.CreatedAtUtc < fromUtc)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenByDescending(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var openingBalance = lastEntryBeforeFrom?.BalanceAfter ?? 0.00m;

        // 2. Query all ledger entries within [fromUtc, toUtc]
        var entries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Include(l => l.Transaction)
            .Where(l => l.WalletId == wallet.Id && l.CreatedAtUtc >= fromUtc && l.CreatedAtUtc <= toUtc)
            .OrderBy(l => l.CreatedAtUtc)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        // 3. Compute reconciliation totals
        var totalDebits = entries.Where(e => e.EntryType == LedgerEntryType.Debit).Sum(e => e.Amount);
        var totalCredits = entries.Where(e => e.EntryType == LedgerEntryType.Credit).Sum(e => e.Amount);
        var netMovement = totalCredits - totalDebits;
        var closingBalance = openingBalance + netMovement;

        // 4. Map entries
        var statementEntries = entries.Select(e => new StatementEntryDto(
            Date: e.CreatedAtUtc,
            TransactionId: e.TransactionId,
            Reference: e.Transaction?.Reference ?? e.TransactionId.ToString()[..8],
            Type: e.Transaction?.Type.ToString() ?? "Adjustment",
            Description: e.Transaction?.Description ?? (e.EntryType == LedgerEntryType.Credit ? "Credit Movement" : "Debit Movement"),
            Debit: e.EntryType == LedgerEntryType.Debit ? e.Amount : null,
            Credit: e.EntryType == LedgerEntryType.Credit ? e.Amount : null,
            BalanceAfter: e.BalanceAfter,
            Currency: e.Currency
        )).ToList();

        // 5. Verification Hash (SHA-256)
        using var sha256 = SHA256.Create();
        var rawData = $"{wallet.Id}:{fromUtc:O}:{toUtc:O}:{openingBalance:F2}:{totalCredits:F2}:{totalDebits:F2}:{closingBalance:F2}:{statementEntries.Count}";
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        var verificationHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var userName = $"{wallet.User?.FirstName} {wallet.User?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = "Valued Customer";
        }

        return new StatementSummaryDto(
            WalletId: wallet.Id,
            UserId: wallet.UserId,
            UserName: userName,
            UserEmail: wallet.User?.Email ?? "customer@payflow.local",
            Currency: wallet.Currency,
            FromDate: fromUtc,
            ToDate: toUtc,
            GeneratedAt: DateTime.UtcNow,
            OpeningBalance: openingBalance,
            TotalCredits: totalCredits,
            TotalDebits: totalDebits,
            NetMovement: netMovement,
            ClosingBalance: closingBalance,
            TransactionCount: statementEntries.Count,
            VerificationHash: verificationHash,
            Entries: statementEntries
        );
    }

    public async Task<byte[]> GenerateCsvStatementAsync(
        Guid userId,
        Guid? walletId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        var statementData = await GetStatementDataAsync(userId, walletId, fromDate, toDate, cancellationToken);
        return _reportGenerator.GenerateCsv(statementData);
    }

    public async Task<byte[]> GeneratePdfStatementAsync(
        Guid userId,
        Guid? walletId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        var statementData = await GetStatementDataAsync(userId, walletId, fromDate, toDate, cancellationToken);
        return _reportGenerator.GeneratePdf(statementData);
    }
}
