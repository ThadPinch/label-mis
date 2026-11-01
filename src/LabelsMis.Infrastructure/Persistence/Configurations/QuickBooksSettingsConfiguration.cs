using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class QuickBooksSettingsConfiguration : IEntityTypeConfiguration<QuickBooksSettings>
{
    public void Configure(EntityTypeBuilder<QuickBooksSettings> builder)
    {
        builder.ToTable("QuickBooksSettings");
        builder.ConfigureAuditableEntity();
        builder.Property(s => s.SalesItem).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ShippingItem).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DesignItem).HasMaxLength(100).IsRequired();
        builder.Property(s => s.RushItem).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DieCreationItem).HasMaxLength(100).IsRequired();
        builder.Property(s => s.RollLabelClass).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ShrinkSleevesClass).HasMaxLength(100).IsRequired();
        builder.Property(s => s.BuyoutClass).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DesignKeywords).HasMaxLength(500).IsRequired();
        builder.Property(s => s.RushKeywords).HasMaxLength(500).IsRequired();
        builder.Property(s => s.DieCreationKeywords).HasMaxLength(500).IsRequired();
        builder.Property(s => s.OutsourcedSubstrateCode).HasMaxLength(50).IsRequired();
    }
}
