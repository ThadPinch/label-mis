using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class SalesOrderChargeConfiguration : IEntityTypeConfiguration<SalesOrderCharge>
{
    public void Configure(EntityTypeBuilder<SalesOrderCharge> builder)
    {
        builder.ToTable("SalesOrderCharge");
        builder.ConfigureAuditableEntity();

        builder.Property(c => c.LineNumber).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Quantity).IsRequired();
        builder.Property(c => c.UnitPrice).HasMoneyPrecision();
        builder.Property(c => c.LineTotal).HasMoneyPrecision();

        builder.HasIndex(c => new { c.SalesOrderId, c.LineNumber }).IsUnique();
        builder.HasIndex(c => c.SourceEstimateChargeId);

        builder.HasOne(c => c.SalesOrder)
            .WithMany(o => o.Charges)
            .HasForeignKey(c => c.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EstimateCharge>()
            .WithMany()
            .HasForeignKey(c => c.SourceEstimateChargeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
