using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.HasKey(w => w.Id);

        builder.ToTable(t => t.HasCheckConstraint("CK_Wallets_Balance_NonNegative", "\"Balance\" >= 0"));

        builder.HasIndex(w => w.UserId)
            .IsUnique();

        builder.Property(w => w.Balance)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(w => w.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(w => w.Version)
            .IsConcurrencyToken();

        builder.Property(w => w.IsFrozen)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(w => w.FreezeReason)
            .HasMaxLength(256);
    }
}
