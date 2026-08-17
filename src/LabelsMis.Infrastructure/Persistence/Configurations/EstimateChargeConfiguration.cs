using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class EstimateChargeConfiguration : IEntityTypeConfiguration<EstimateCharge>
{
    public void Configure(EntityTypeBuilder<EstimateCharge> builder)
    {
        builder.ToTable("EstimateCharge");
        builder.ConfigureAuditableEntity();

        builder.Property(c => c.LineNumber).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Quantity).IsRequired();
        builder.Property(c => c.UnitPrice).HasMoneyPrecision();
        builder.Property(c => c.LineTotal).HasMoneyPrecision();
        builder.Property(c => c.IsOutsourced).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.OutsourceQuoteNumber).HasMaxLength(100);
        builder.Property(c => c.OutsourceCost).HasPrecision(18, 4);
        builder.Property(c => c.OutsourcePrivateNotes).HasMaxLength(2000);

        builder.HasIndex(c => new { c.EstimateId, c.LineNumber }).IsUnique();

        builder.HasOne(c => c.OutsourceVendor)
            .WithMany()
            .HasForeignKey(c => c.OutsourceVendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Estimate)
            .WithMany(e => e.Charges)
            .HasForeignKey(c => c.EstimateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
