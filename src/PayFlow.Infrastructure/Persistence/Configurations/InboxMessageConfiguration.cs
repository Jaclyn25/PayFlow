using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.MessageId)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(i => i.MessageId)
            .IsUnique();

        builder.Property(i => i.EventType)
            .IsRequired()
            .HasMaxLength(100);
    }
}
