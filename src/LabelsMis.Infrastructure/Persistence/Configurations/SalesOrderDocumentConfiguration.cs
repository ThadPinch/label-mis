using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class SalesOrderDocumentConfiguration : IEntityTypeConfiguration<SalesOrderDocument>
{
    public void Configure(EntityTypeBuilder<SalesOrderDocument> builder)
    {
        builder.ToTable("SalesOrderDocument");
        builder.ConfigureAuditableEntity();

        builder.Property(d => d.FileKey).HasMaxLength(500).IsRequired();
        builder.Property(d => d.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(d => d.FileSizeBytes).IsRequired();

        builder.HasIndex(d => d.SalesOrderId);

        builder.HasOne(d => d.SalesOrder)
            .WithMany()
            .HasForeignKey(d => d.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
