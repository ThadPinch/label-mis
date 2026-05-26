using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class InkConfiguration : IEntityTypeConfiguration<Ink>
{
    public void Configure(EntityTypeBuilder<Ink> builder)
    {
        builder.ToTable("Ink");
        builder.ConfigureMasterDataEntity();

        builder.Property(i => i.Code).HasMaxLength(50).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(500).IsRequired();
        builder.Property(i => i.ClickRatePer1000).HasMoneyPrecision();

        builder.HasIndex(i => i.Code).IsUnique();
    }
}
