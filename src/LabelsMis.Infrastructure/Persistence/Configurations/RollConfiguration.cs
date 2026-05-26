using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class RollConfiguration : IEntityTypeConfiguration<Roll>
{
    public void Configure(EntityTypeBuilder<Roll> builder)
    {
        builder.ToTable("Roll");
        builder.ConfigureAuditableEntity();

        builder.Property(r => r.RollBarcode).HasMaxLength(50).IsRequired();
        builder.Property(r => r.SupplierLotNumber).HasMaxLength(100).IsRequired();
        builder.Property(r => r.WidthIn).HasDimensionPrecision();
        builder.Property(r => r.OriginalLengthLf).HasQuantityPrecision();
        builder.Property(r => r.RemainingLengthLf).HasQuantityPrecision();
        builder.Property(r => r.ReceivedAt).IsRequired();
        builder.Property(r => r.Location).HasMaxLength(100);
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(4000);

        builder.HasIndex(r => r.RollBarcode).IsUnique();
        builder.HasIndex(r => r.StockId);
        builder.HasIndex(r => r.Status);

        builder.HasOne(r => r.Stock)
            .WithMany()
            .HasForeignKey(r => r.StockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Receipt)
            .WithMany()
            .HasForeignKey(r => r.ReceiptId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(r => r.Movements)
            .WithOne(m => m.Roll)
            .HasForeignKey(m => m.RollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Movements).HasField("_movements");
    }
}
