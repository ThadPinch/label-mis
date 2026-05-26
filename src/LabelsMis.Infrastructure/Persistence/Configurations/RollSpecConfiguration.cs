using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class RollSpecConfiguration : IEntityTypeConfiguration<RollSpec>
{
    public void Configure(EntityTypeBuilder<RollSpec> builder)
    {
        builder.ToTable("RollSpec");
        builder.ConfigureAuditableEntity();

        builder.Property(r => r.LabelsPerRoll).IsRequired();
        builder.Property(r => r.CoreSizeIn).HasDimensionPrecision();
        builder.Property(r => r.UnwindPosition).IsRequired();
        builder.Property(r => r.MaxOdIn).HasDimensionPrecision();
        builder.Property(r => r.RollsPerCase).IsRequired();
        builder.Property(r => r.CaseLabelFormat).HasMaxLength(200);

        builder.HasIndex(r => r.ProductId).IsUnique();
    }
}
