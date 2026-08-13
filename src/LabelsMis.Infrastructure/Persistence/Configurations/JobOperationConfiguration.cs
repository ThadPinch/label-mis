using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class JobOperationConfiguration : IEntityTypeConfiguration<JobOperation>
{
    public void Configure(EntityTypeBuilder<JobOperation> builder)
    {
        builder.ToTable("JobOperation");
        builder.ConfigureAuditableEntity();

        builder.Property(o => o.Sequence).IsRequired();
        builder.Property(o => o.OperationType).IsRequired();
        builder.Property(o => o.EquipmentType).IsRequired();
        builder.Property(o => o.PlannedMinutes).HasDimensionPrecision();
        builder.Property(o => o.Status).IsRequired();
        builder.Property(o => o.GoodCount).IsRequired();
        builder.Property(o => o.WasteCount).IsRequired();
        builder.Property(o => o.DowntimeMinutes).HasDimensionPrecision();
        builder.Property(o => o.ActualMinutes).HasDimensionPrecision();

        builder.HasIndex(o => new { o.JobId, o.Sequence }).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(o => o.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Roll>()
            .WithMany()
            .HasForeignKey(o => o.ScannedRollId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.TimeEntries)
            .WithOne(t => t.JobOperation)
            .HasForeignKey(t => t.JobOperationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.TimeEntries).HasField("_timeEntries");
    }
}
