using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class JobFileConfiguration : IEntityTypeConfiguration<JobFile>
{
    public void Configure(EntityTypeBuilder<JobFile> builder)
    {
        builder.ToTable("JobFile");
        builder.ConfigureAuditableEntity();

        builder.Property(f => f.Category).IsRequired();
        builder.Property(f => f.FileKey).HasMaxLength(500).IsRequired();
        builder.Property(f => f.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(f => f.FileSizeBytes).IsRequired();
        builder.Property(f => f.Notes).HasMaxLength(500);

        builder.Ignore(f => f.IsArchived);

        builder.HasIndex(f => f.JobId);

        builder.HasOne(f => f.Job)
            .WithMany()
            .HasForeignKey(f => f.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
