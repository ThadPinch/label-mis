using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class PressConfiguration : IEntityTypeConfiguration<Press>
{
    public void Configure(EntityTypeBuilder<Press> builder)
    {
        builder.ToTable("Press");
        builder.ConfigureMasterDataEntity();

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.WebWidthIn).HasDimensionPrecision();
        builder.Property(p => p.MinWebWidthIn).HasDimensionPrecision();
        builder.Property(p => p.MaxWebWidthIn).HasDimensionPrecision();
        builder.Property(p => p.MaxImageWidthIn).HasDimensionPrecision();
        builder.Property(p => p.FrameRepeatIn).HasDimensionPrecision();
        builder.Property(p => p.MaxImageLengthIn).HasDimensionPrecision();
        builder.Property(p => p.MaxRepeatIn).HasDimensionPrecision();
        builder.Property(p => p.MinRepeatIn).HasDimensionPrecision();
        builder.Property(p => p.SpeedFpm).HasDimensionPrecision();
        builder.Property(p => p.SetupMinutes).HasDimensionPrecision();
        builder.Property(p => p.CostPerHour).HasMoneyPrecision();

        builder.HasIndex(p => p.Code).IsUnique();
    }
}
