using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Job");
        builder.ConfigureAuditableEntity();

        builder.Property(j => j.JobNumber).HasMaxLength(20).IsRequired();
        builder.Property(j => j.QuantityOrdered).IsRequired();
        builder.Property(j => j.QuantityPlanned).IsRequired();
        builder.Property(j => j.Status).IsRequired();
        builder.Property(j => j.Priority).IsRequired();
        builder.Property(j => j.Notes).HasMaxLength(4000);
        builder.Property(j => j.IsOutsourced).IsRequired().HasDefaultValue(false);

        builder.OwnsLabelSpec(j => j.Spec);

        builder.HasIndex(j => j.JobNumber).IsUnique();
        builder.HasIndex(j => j.Status);
        builder.HasIndex(j => j.SalesOrderLineId);
        builder.HasIndex(j => j.ProductId);

        builder.HasOne(j => j.SalesOrderLine)
            .WithMany()
            .HasForeignKey(j => j.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(j => j.Product)
            .WithMany()
            .HasForeignKey(j => j.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Press>()
            .WithMany()
            .HasForeignKey(j => j.ScheduledPressId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(j => j.Operations)
            .WithOne(o => o.Job)
            .HasForeignKey(o => o.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(j => j.MaterialUsages)
            .WithOne(u => u.Job)
            .HasForeignKey(u => u.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(j => j.Operations).HasField("_operations");
        builder.Navigation(j => j.MaterialUsages).HasField("_materialUsages");
    }
}
