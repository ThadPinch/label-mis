using System.Linq.Expressions;
using LabelsMis.Domain.Common;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

internal static class EntityConfigurationExtensions
{
    /// <summary>Maps a <see cref="LabelSpec"/> as an owned type flattened onto the owner's table with
    /// <c>Spec*</c> column names. Optional (nullable) so it can be added to tables with existing rows
    /// and populated by backfill. See docs/labelspec-refactor.md.</summary>
    public static void OwnsLabelSpec<TOwner>(
        this EntityTypeBuilder<TOwner> builder,
        Expression<Func<TOwner, LabelSpec?>> navigation)
        where TOwner : class
    {
        builder.OwnsOne(navigation, spec =>
        {
            spec.Property(s => s.LabelAcrossIn).HasColumnName("SpecLabelAcrossIn").HasDimensionPrecision();
            spec.Property(s => s.LabelAroundIn).HasColumnName("SpecLabelAroundIn").HasDimensionPrecision();
            spec.Property(s => s.CornerRadiusIn).HasColumnName("SpecCornerRadiusIn").HasDimensionPrecision();
            spec.Property(s => s.GutterAcrossIn).HasColumnName("SpecGutterAcrossIn").HasDimensionPrecision();
            spec.Property(s => s.GutterAroundIn).HasColumnName("SpecGutterAroundIn").HasDimensionPrecision();
            spec.Property(s => s.BleedIn).HasColumnName("SpecBleedIn").HasDimensionPrecision();
            spec.Property(s => s.SubstrateId).HasColumnName("SpecSubstrateId");
            spec.Property(s => s.DieId).HasColumnName("SpecDieId");
            spec.Property(s => s.InkSet).HasColumnName("SpecInkSet");
            spec.Property(s => s.WhiteHits).HasColumnName("SpecWhiteHits");
            spec.Property(s => s.WhiteCoveragePct).HasColumnName("SpecWhiteCoveragePct").HasPrecision(18, 4);
            spec.Property(s => s.SpotsJson).HasColumnName("SpecSpotsJson").HasColumnType("jsonb");
            spec.Property(s => s.FinishingOperationsJson).HasColumnName("SpecFinishingOperationsJson").HasColumnType("jsonb");
            spec.Property(s => s.SetupWasteImpressions).HasColumnName("SpecSetupWasteImpressions").HasQuantityPrecision();
            spec.Property(s => s.RunningWastePct).HasColumnName("SpecRunningWastePct").HasMoneyPrecision();
            spec.Property(s => s.MaxLabelsAcrossOverride).HasColumnName("SpecMaxLabelsAcrossOverride");
            spec.Property(s => s.LabelOrientationOverride).HasColumnName("SpecLabelOrientationOverride");
            spec.Property(s => s.ArtworkFilePath).HasColumnName("SpecArtworkFilePath").HasMaxLength(500);
            spec.Property(s => s.ShrinkLayflatIn).HasPrecision(10, 4);
        });
    }

    /// <summary>Maps an <see cref="ImpositionTemplate"/> as an owned type flattened onto the owner's
    /// table with <c>Imposition*</c> column names. Optional so existing rows stay null until prepress
    /// first saves a template.</summary>
    public static void OwnsImpositionTemplate<TOwner>(
        this EntityTypeBuilder<TOwner> builder,
        Expression<Func<TOwner, ImpositionTemplate?>> navigation)
        where TOwner : class
    {
        builder.OwnsOne(navigation, t =>
        {
            t.Property(x => x.LabelAcrossIn).HasColumnName("ImpositionLabelAcrossIn").HasDimensionPrecision();
            t.Property(x => x.LabelAroundIn).HasColumnName("ImpositionLabelAroundIn").HasDimensionPrecision();
            t.Property(x => x.CornerRadiusIn).HasColumnName("ImpositionCornerRadiusIn").HasDimensionPrecision();
            t.Property(x => x.GutterAcrossIn).HasColumnName("ImpositionGutterAcrossIn").HasDimensionPrecision();
            t.Property(x => x.GutterAroundIn).HasColumnName("ImpositionGutterAroundIn").HasDimensionPrecision();
            t.Property(x => x.BleedIn).HasColumnName("ImpositionBleedIn").HasDimensionPrecision();
            t.Property(x => x.LabelsAcross).HasColumnName("ImpositionLabelsAcross");
            t.Property(x => x.LabelsAround).HasColumnName("ImpositionLabelsAround");
            t.Property(x => x.Orientation).HasColumnName("ImpositionOrientation");
            t.Property(x => x.WebWidthIn).HasColumnName("ImpositionWebWidthIn").HasDimensionPrecision();
            t.Property(x => x.CrossWebOffsetIn).HasColumnName("ImpositionCrossWebOffsetIn").HasDimensionPrecision();
            t.Property(x => x.EyeMarks).HasColumnName("ImpositionEyeMarks");
            t.Property(x => x.EyeMarkWidthIn).HasColumnName("ImpositionEyeMarkWidthIn").HasDimensionPrecision();
            t.Property(x => x.EyeMarkHeightIn).HasColumnName("ImpositionEyeMarkHeightIn").HasDimensionPrecision();
            t.Property(x => x.IncludeDieLines).HasColumnName("ImpositionIncludeDieLines");
            t.Property(x => x.IncludeSlug).HasColumnName("ImpositionIncludeSlug");
        });
    }

    public static void ConfigureAuditableEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : EntityBase
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId)
            .IsRequired()
            .HasDefaultValue(TenantConstants.DefaultTenantId);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.CreatedById)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.ModifiedById)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public static void ConfigureMasterDataEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : MasterDataEntity
    {
        builder.ConfigureAuditableEntity();
        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
    }

    public static PropertyBuilder<decimal> HasMoneyPrecision(this PropertyBuilder<decimal> builder) =>
        builder.HasPrecision(18, 4);

    public static PropertyBuilder<decimal> HasDimensionPrecision(this PropertyBuilder<decimal> builder) =>
        builder.HasPrecision(10, 4);

    public static PropertyBuilder<decimal> HasQuantityPrecision(this PropertyBuilder<decimal> builder) =>
        builder.HasPrecision(14, 4);
}
