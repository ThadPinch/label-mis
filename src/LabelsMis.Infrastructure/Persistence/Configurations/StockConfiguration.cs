using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stock");
        builder.ConfigureMasterDataEntity();

        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500).IsRequired();
        builder.Property(s => s.FaceMaterial).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Adhesive).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Liner).HasMaxLength(200).IsRequired();
        builder.Property(s => s.TotalCaliperMil).HasDimensionPrecision();
        builder.Property(s => s.WidthIn).HasDimensionPrecision();
        builder.Property(s => s.SupplierPartNumber).HasMaxLength(100);
        builder.Property(s => s.CostPerMsi).HasMoneyPrecision();
        builder.Property(s => s.MinOrderQtyLf).HasQuantityPrecision();
        builder.Property(s => s.StockType).HasConversion<int>().IsRequired();

        builder.HasIndex(s => s.Code).IsUnique();

        builder.HasOne(s => s.Supplier)
            .WithMany()
            .HasForeignKey(s => s.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.CostHistory)
            .WithOne(h => h.Stock)
            .HasForeignKey(h => h.StockId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.CostHistory).HasField("_costHistory");
    }
}
