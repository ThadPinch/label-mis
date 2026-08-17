using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class OutsourcedItemConfiguration : IEntityTypeConfiguration<OutsourcedItem>
{
    public void Configure(EntityTypeBuilder<OutsourcedItem> builder)
    {
        builder.ToTable("OutsourcedItem");
        builder.ConfigureAuditableEntity();

        builder.Property(o => o.QuoteNumber).HasMaxLength(100);
        builder.Property(o => o.VendorCost).HasPrecision(18, 4);
        builder.Property(o => o.PrivateNotes).HasMaxLength(2000);

        // One tracking record per order line / per charge.
        builder.HasIndex(o => o.SalesOrderLineId).IsUnique();
        builder.HasIndex(o => o.SalesOrderChargeId).IsUnique();
        builder.HasIndex(o => o.SalesOrderId);
        builder.HasIndex(o => o.VendorId);

        builder.HasOne(o => o.SalesOrder)
            .WithMany()
            .HasForeignKey(o => o.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.SalesOrderLine)
            .WithOne(l => l.OutsourcedItem)
            .HasForeignKey<OutsourcedItem>(o => o.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.SalesOrderCharge)
            .WithOne(c => c.OutsourcedItem)
            .HasForeignKey<OutsourcedItem>(o => o.SalesOrderChargeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Vendor)
            .WithMany()
            .HasForeignKey(o => o.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Receipts)
            .WithOne(r => r.OutsourcedItem)
            .HasForeignKey(r => r.OutsourcedItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Receipts).HasField("_receipts");
    }
}
