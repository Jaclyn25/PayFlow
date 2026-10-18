using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.ResourceId)
            .HasMaxLength(100);

        builder.Property(a => a.IpAddress)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(a => a.UserAgent)
            .HasMaxLength(512);

        builder.Property(a => a.FailureReason)
            .HasMaxLength(500);

        builder.Property(a => a.TimestampUtc)
            .IsRequired();

        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Action);
        builder.HasIndex(a => a.TimestampUtc);
    }
}
