using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ToTable("ShippingMethod");
        builder.ConfigureMasterDataEntity();

        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.Property(m => m.MethodType).HasConversion<int>();
        builder.Property(m => m.Price).HasMoneyPrecision();
        builder.Property(m => m.RequiresAddress).IsRequired().HasDefaultValue(true);

        builder.HasIndex(m => m.Name).IsUnique();
    }
}
