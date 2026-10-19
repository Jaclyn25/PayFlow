using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PayFlow.Domain.Entities;

namespace PayFlow.Application.Common.Interfaces;

public interface IPayFlowDbContext
{
    DbSet<User> Users { get; }
    DbSet<Wallet> Wallets { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<TwoFactorRecoveryCode> TwoFactorRecoveryCodes { get; }


    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
