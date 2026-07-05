using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("SalesOrderLine");
        builder.ConfigureAuditableEntity();

        builder.Property(l => l.LineNumber).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitPrice).HasMoneyPrecision();
        builder.Property(l => l.LineTotal).HasMoneyPrecision();
        builder.Property(l => l.LineNotes).HasMaxLength(1000);

        builder.OwnsLabelSpec(l => l.Spec);

        builder.HasIndex(l => new { l.SalesOrderId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.SourceEstimateLineId);

        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.SourceEstimateLine)
            .WithMany()
            .HasForeignKey(l => l.SourceEstimateLineId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
