using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class DieConfiguration : IEntityTypeConfiguration<Die>
{
    public void Configure(EntityTypeBuilder<Die> builder)
    {
        builder.ToTable("Die");
        builder.ConfigureMasterDataEntity();

        builder.Property(d => d.Description).HasMaxLength(500).IsRequired();
        builder.Property(d => d.Shape).HasMaxLength(100);
        builder.Property(d => d.LabelAcrossIn).HasDimensionPrecision();
        builder.Property(d => d.LabelAroundIn).HasDimensionPrecision();
        builder.Property(d => d.CornerRadiusIn).HasDimensionPrecision();
        builder.Property(d => d.GutterAcrossIn).HasDimensionPrecision();
        builder.Property(d => d.GutterAroundIn).HasDimensionPrecision();
        builder.Property(d => d.RepeatLengthIn).HasDimensionPrecision();
        builder.Property(d => d.WebWidthIn).HasDimensionPrecision();
        builder.Property(d => d.SupplierPartNumber).HasMaxLength(100);
        builder.Property(d => d.Location).HasMaxLength(100);

        builder.HasOne(d => d.Customer)
            .WithMany()
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.Supplier)
            .WithMany()
            .HasForeignKey(d => d.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(d => d.UsageHistory)
            .WithOne(u => u.Die)
            .HasForeignKey(u => u.DieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.UsageHistory).HasField("_usageHistory");
    }
}
