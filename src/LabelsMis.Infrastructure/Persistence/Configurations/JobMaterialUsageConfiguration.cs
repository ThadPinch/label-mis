using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class JobMaterialUsageConfiguration : IEntityTypeConfiguration<JobMaterialUsage>
{
    public void Configure(EntityTypeBuilder<JobMaterialUsage> builder)
    {
        builder.ToTable("JobMaterialUsage");
        builder.ConfigureAuditableEntity();

        builder.Property(u => u.QuantityUsedLf).HasQuantityPrecision();
        builder.Property(u => u.UsedAt).IsRequired();
        builder.Property(u => u.Notes).HasMaxLength(1000);

        builder.HasIndex(u => u.JobId);
        builder.HasIndex(u => u.StockId);
        builder.HasIndex(u => u.RollId);

        builder.HasOne(u => u.Stock)
            .WithMany()
            .HasForeignKey(u => u.StockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Roll)
            .WithMany()
            .HasForeignKey(u => u.RollId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
