using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(u => u.Email)
            .IsUnique();

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(PayFlow.Domain.Enums.UserRole.Customer)
            .HasSentinel((PayFlow.Domain.Enums.UserRole)(-1))
            .IsRequired();

        builder.Property(u => u.KycTier)
            .HasConversion<int>()
            .HasDefaultValue(PayFlow.Domain.Enums.KycTier.Tier1)
            .HasSentinel((PayFlow.Domain.Enums.KycTier)(-1))
            .IsRequired();

        builder.Property(u => u.TwoFactorEnabled)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(u => u.TwoFactorSecret)
            .HasMaxLength(128);

        builder.HasOne(u => u.Wallet)
            .WithOne(w => w.User)
            .HasForeignKey<Wallet>(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(r => r.User)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.TwoFactorRecoveryCodes)
            .WithOne(r => r.User)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

