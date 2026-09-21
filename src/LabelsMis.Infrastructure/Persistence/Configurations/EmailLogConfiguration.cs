using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("EmailLog");
        builder.ConfigureAuditableEntity();
        builder.Property(e => e.DocumentType).IsRequired();
        builder.Property(e => e.To).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Cc).HasMaxLength(500);
        builder.Property(e => e.Subject).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Error).HasMaxLength(1000);

        builder.HasIndex(e => new { e.DocumentType, e.DocumentId });
        builder.HasIndex(e => e.SalesOrderId);
    }
}
