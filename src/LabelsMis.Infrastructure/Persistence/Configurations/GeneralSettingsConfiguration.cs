using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class GeneralSettingsConfiguration : IEntityTypeConfiguration<GeneralSettings>
{
    public void Configure(EntityTypeBuilder<GeneralSettings> builder)
    {
        builder.ToTable("GeneralSettings");
        builder.ConfigureAuditableEntity();
        builder.Property(s => s.CompanyName).HasMaxLength(200);
        builder.Property(s => s.AddressLine1).HasMaxLength(200);
        builder.Property(s => s.AddressLine2).HasMaxLength(200);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.State).HasMaxLength(100);
        builder.Property(s => s.Zip).HasMaxLength(20);
        builder.Property(s => s.Phone).HasMaxLength(50);
        builder.Property(s => s.Email).HasMaxLength(200);
        builder.Property(s => s.Website).HasMaxLength(200);
        builder.Property(s => s.TermsText).HasMaxLength(2000);
        builder.Property(s => s.LogoContentType).HasMaxLength(100);
    }
}
