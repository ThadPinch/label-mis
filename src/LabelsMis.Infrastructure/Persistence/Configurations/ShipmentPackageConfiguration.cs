using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class ShipmentPackageConfiguration : IEntityTypeConfiguration<ShipmentPackage>
{
    public void Configure(EntityTypeBuilder<ShipmentPackage> builder)
    {
        builder.ToTable("ShipmentPackage");
        builder.ConfigureAuditableEntity();

        builder.Property(p => p.PackageNumber).IsRequired();
        builder.Property(p => p.WeightLb).HasQuantityPrecision();
        builder.Property(p => p.LengthIn).HasDimensionPrecision();
        builder.Property(p => p.WidthIn).HasDimensionPrecision();
        builder.Property(p => p.HeightIn).HasDimensionPrecision();
        builder.Property(p => p.TrackingNumber).HasMaxLength(50);
        builder.Property(p => p.LabelUrl).HasMaxLength(500);
        builder.Property(p => p.DeclaredValue).HasMoneyPrecision();
        builder.Property(p => p.ShippingCost).HasMoneyPrecision();

        builder.HasIndex(p => new { p.ShipmentId, p.PackageNumber }).IsUnique();
        builder.HasIndex(p => p.TrackingNumber)
            .IsUnique()
            .HasFilter("\"TrackingNumber\" IS NOT NULL");

        builder.HasMany(p => p.TrackingEvents)
            .WithOne(e => e.ShipmentPackage)
            .HasForeignKey(e => e.ShipmentPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.TrackingEvents).HasField("_trackingEvents");
    }
}
