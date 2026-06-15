using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class EmailSettingsConfiguration : IEntityTypeConfiguration<EmailSettings>
{
    public void Configure(EntityTypeBuilder<EmailSettings> builder)
    {
        builder.ToTable("EmailSettings");
        builder.ConfigureAuditableEntity();
        builder.Property(s => s.ApiBaseUrl).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Domain).HasMaxLength(200);
        builder.Property(s => s.ApiKey).HasMaxLength(500);
        builder.Property(s => s.FromName).HasMaxLength(200);
        builder.Property(s => s.FromEmail).HasMaxLength(200);
    }
}
