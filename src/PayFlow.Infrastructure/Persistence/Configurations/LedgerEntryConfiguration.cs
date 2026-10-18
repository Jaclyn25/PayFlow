using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.EntryType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(l => l.Amount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(l => l.BalanceAfter)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.HasOne(l => l.Transaction)
            .WithMany()
            .HasForeignKey(l => l.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Wallet)
            .WithMany()
            .HasForeignKey(l => l.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.WalletId);
        builder.HasIndex(l => l.TransactionId);
        builder.HasIndex(l => l.CreatedAtUtc);
    }
}
