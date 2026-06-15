using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class EstimateLineConfiguration : IEntityTypeConfiguration<EstimateLine>
{
    public void Configure(EntityTypeBuilder<EstimateLine> builder)
    {
        builder.ToTable("EstimateLine");
        builder.ConfigureAuditableEntity();

        builder.Property(l => l.LineNumber).IsRequired();
        builder.Property(l => l.ProductDescription).HasMaxLength(500).IsRequired();
        builder.Property(l => l.LabelAcrossIn).HasDimensionPrecision();
        builder.Property(l => l.LabelAroundIn).HasDimensionPrecision();
        builder.Property(l => l.CornerRadiusIn).HasDimensionPrecision();
        builder.Property(l => l.GutterAcrossIn).HasDimensionPrecision();
        builder.Property(l => l.GutterAroundIn).HasDimensionPrecision();
        builder.Property(l => l.BleedIn).HasDimensionPrecision();
        builder.Property(l => l.InkSet).IsRequired();
        builder.Property(l => l.WhiteHits);
        builder.Property(l => l.SilverHits);
        builder.Property(l => l.WhiteCoveragePct).HasPrecision(18, 4);
        builder.Property(l => l.SilverCoveragePct).HasPrecision(18, 4);
        builder.Property(l => l.FinishingOperationsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(l => l.SetupWasteImpressions).HasQuantityPrecision();
        builder.Property(l => l.RunningWastePct).HasMoneyPrecision();
        builder.Property(l => l.LineNotes).HasMaxLength(2000);
        builder.Property(l => l.MarkupPctOverride).HasPrecision(18, 4);
        builder.Property(l => l.MaxLabelsAcrossOverride);
        builder.Property(l => l.LabelOrientationOverride);

        builder.HasIndex(l => new { l.EstimateId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.SourceProductId);

        builder.HasOne(l => l.Substrate)
            .WithMany()
            .HasForeignKey(l => l.SubstrateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(l => l.SourceProductId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Navigation(l => l.QuantityBreaks).HasField("_quantityBreaks");
    }
}
