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

        builder.HasIndex(c => new { c.EstimateId, c.LineNumber }).IsUnique();

        builder.HasOne(c => c.Estimate)
            .WithMany(e => e.Charges)
            .HasForeignKey(c => c.EstimateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
