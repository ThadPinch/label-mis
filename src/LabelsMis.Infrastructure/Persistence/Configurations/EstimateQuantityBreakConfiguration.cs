using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class EstimateQuantityBreakConfiguration : IEntityTypeConfiguration<EstimateQuantityBreak>
{
    public void Configure(EntityTypeBuilder<EstimateQuantityBreak> builder)
    {
        builder.ToTable("EstimateQuantityBreak");
        builder.ConfigureAuditableEntity();

        builder.Property(q => q.Quantity).IsRequired();
        builder.Property(q => q.UnitPrice).HasMoneyPrecision();
        builder.Property(q => q.TotalPrice).HasMoneyPrecision();
        builder.Property(q => q.CalculatedCost).HasMoneyPrecision();
        builder.Property(q => q.MarginPct).HasMoneyPrecision();
        builder.Property(q => q.MarkupPctOverride).HasPrecision(18, 4);
        builder.Property(q => q.CostBreakdownJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(q => new { q.EstimateLineId, q.Quantity }).IsUnique();

        builder.HasOne(q => q.EstimateLine)
            .WithMany(l => l.QuantityBreaks)
            .HasForeignKey(q => q.EstimateLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
