using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class FinishingOperationConfiguration : IEntityTypeConfiguration<FinishingOperation>
{
    public void Configure(EntityTypeBuilder<FinishingOperation> builder)
    {
        builder.ToTable("FinishingOperation");
        builder.ConfigureMasterDataEntity();

        builder.Property(o => o.Code).HasMaxLength(50).IsRequired();
        builder.Property(o => o.Description).HasMaxLength(500).IsRequired();
        builder.Property(o => o.DefaultSetupMinutes).HasDimensionPrecision();
        builder.Property(o => o.DefaultRunSpeedFpm).HasDimensionPrecision();
        builder.Property(o => o.EquipmentName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.CostPerHour).HasMoneyPrecision();

        builder.HasIndex(o => o.Code).IsUnique();
    }
}
