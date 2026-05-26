using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrder");
        builder.ConfigureAuditableEntity();

        builder.Property(o => o.PoNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.OrderedAt).IsRequired();
        builder.Property(o => o.Status).IsRequired();
        builder.Property(o => o.Notes).HasMaxLength(4000);

        builder.HasIndex(o => o.PoNumber).IsUnique();
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.SupplierId);

        builder.HasOne(o => o.Supplier)
            .WithMany()
            .HasForeignKey(o => o.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Lines)
            .WithOne(l => l.PurchaseOrder)
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Lines).HasField("_lines");
    }
}
