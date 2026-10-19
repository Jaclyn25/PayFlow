using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class TwoFactorRecoveryCodeConfiguration : IEntityTypeConfiguration<TwoFactorRecoveryCode>
{
    public void Configure(EntityTypeBuilder<TwoFactorRecoveryCode> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.CodeHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.UserId, r.CodeHash });

        builder.Property(r => r.IsUsed)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired();
    }
}
