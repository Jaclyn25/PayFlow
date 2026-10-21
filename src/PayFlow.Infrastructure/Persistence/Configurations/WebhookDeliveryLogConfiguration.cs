using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class WebhookDeliveryLogConfiguration : IEntityTypeConfiguration<WebhookDeliveryLog>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryLog> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.Payload)
            .IsRequired();

        builder.Property(d => d.Signature)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(d => d.AttemptCount)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(d => d.DurationMs)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(d => d.ResponseBody)
            .HasMaxLength(4000);

        builder.Property(d => d.Error)
            .HasMaxLength(4000);

        builder.Property(d => d.IsSuccess)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne(d => d.WebhookSubscription)
            .WithMany(w => w.DeliveryLogs)
            .HasForeignKey(d => d.WebhookSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.WebhookSubscriptionId);
        builder.HasIndex(d => new { d.IsSuccess, d.AttemptCount, d.NextRetryAtUtc });
    }
}
