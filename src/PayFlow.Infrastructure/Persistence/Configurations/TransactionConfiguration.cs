using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.ToTable(t => t.HasCheckConstraint("CK_Transactions_Amount_Positive", "\"Amount\" > 0"));

        builder.HasIndex(t => t.Reference)
            .IsUnique();

        builder.Property(t => t.Reference)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(t => t.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(t => t.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(t => t.Description)
            .HasMaxLength(255);

        builder.Property(t => t.IdempotencyKey)
            .HasMaxLength(100);

        builder.HasOne(t => t.SenderWallet)
            .WithMany()
            .HasForeignKey(t => t.SenderWalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ReceiverWallet)
            .WithMany()
            .HasForeignKey(t => t.ReceiverWalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.SenderWalletId);
        builder.HasIndex(t => t.ReceiverWalletId);
        builder.HasIndex(t => t.CreatedAtUtc);
    }
}
