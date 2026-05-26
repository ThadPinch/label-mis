using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class StorageSettingsConfiguration : IEntityTypeConfiguration<StorageSettings>
{
    public void Configure(EntityTypeBuilder<StorageSettings> builder)
    {
        builder.ToTable("StorageSettings");
        builder.ConfigureAuditableEntity();
        builder.Property(s => s.ServiceUrl).HasMaxLength(500);
        builder.Property(s => s.BucketName).HasMaxLength(200);
        builder.Property(s => s.AccessKey).HasMaxLength(200);
        builder.Property(s => s.SecretKey).HasMaxLength(500);
        builder.Property(s => s.Region).HasMaxLength(100);
        builder.Property(s => s.ArtworkKeyPrefix).HasMaxLength(200).IsRequired();
    }
}
