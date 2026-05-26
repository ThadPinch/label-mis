using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLine");
        builder.ConfigureAuditableEntity();

        builder.Property(l => l.LineNumber).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitPrice).HasMoneyPrecision();
        builder.Property(l => l.LineTotal).HasMoneyPrecision();
        builder.Property(l => l.TaxCode).HasMaxLength(20);

        builder.HasIndex(l => new { l.InvoiceId, l.LineNumber }).IsUnique();

        builder.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(l => l.SalesOrderLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(l => l.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
