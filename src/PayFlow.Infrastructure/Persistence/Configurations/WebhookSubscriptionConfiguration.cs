using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Url)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(w => w.SecretKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(w => w.Description)
            .HasMaxLength(500);

        builder.Property(w => w.EventsList)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(w => w.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasOne(w => w.User)
            .WithMany(u => u.WebhookSubscriptions)
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => w.UserId);
        builder.HasIndex(w => w.IsActive);
    }
}
