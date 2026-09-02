using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Key)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(i => new { i.UserId, i.Key })
            .IsUnique()
            .HasDatabaseName("IX_IdempotencyRecords_UserId_Key");

        builder.Property(i => i.ResponseBody)
            .IsRequired();
    }
}
