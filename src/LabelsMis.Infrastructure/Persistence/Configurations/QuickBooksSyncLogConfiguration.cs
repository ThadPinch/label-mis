using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class QuickBooksSyncLogConfiguration : IEntityTypeConfiguration<QuickBooksSyncLog>
{
    public void Configure(EntityTypeBuilder<QuickBooksSyncLog> builder)
    {
        builder.ToTable("QuickBooksSyncLog");
        builder.ConfigureAuditableEntity();
        builder.Property(l => l.Operation).HasMaxLength(QuickBooksSyncLog.MaxOperationLength).IsRequired();
        builder.Property(l => l.Succeeded).IsRequired();
        builder.Property(l => l.Automatic).IsRequired().HasDefaultValue(false);
        builder.Property(l => l.QuickBooksId).HasMaxLength(50);
        builder.Property(l => l.ErrorCode).HasMaxLength(50);
        builder.Property(l => l.Message).HasMaxLength(QuickBooksSyncLog.MaxMessageLength);
        builder.Property(l => l.IntuitTid).HasMaxLength(100);

        // No foreign keys to Invoice or Customer: the log must outlive whatever it describes.
        builder.HasIndex(l => l.CreatedAt);
        builder.HasIndex(l => l.InvoiceId);
    }
}
