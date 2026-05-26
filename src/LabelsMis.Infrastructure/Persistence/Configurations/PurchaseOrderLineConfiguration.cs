using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLine");
        builder.ConfigureAuditableEntity();

        builder.Property(l => l.LineNumber).IsRequired();
        builder.Property(l => l.QuantityLf).HasQuantityPrecision();
        builder.Property(l => l.UnitCost).HasMoneyPrecision();
        builder.Property(l => l.LineTotal).HasMoneyPrecision();
        builder.Property(l => l.QuantityReceivedLf).HasQuantityPrecision();

        builder.HasIndex(l => new { l.PurchaseOrderId, l.LineNumber }).IsUnique();

        builder.HasOne(l => l.Stock)
            .WithMany()
            .HasForeignKey(l => l.StockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Receipts)
            .WithOne(r => r.PoLine)
            .HasForeignKey(r => r.PoLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(l => l.Receipts).HasField("_receipts");
    }
}
