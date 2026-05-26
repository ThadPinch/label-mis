using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class JobTimeEntryConfiguration : IEntityTypeConfiguration<JobTimeEntry>
{
    public void Configure(EntityTypeBuilder<JobTimeEntry> builder)
    {
        builder.ToTable("JobTimeEntry");
        builder.ConfigureAuditableEntity();

        builder.Property(t => t.ClockedInAt).IsRequired();

        builder.HasIndex(t => t.JobOperationId);
        builder.HasIndex(t => t.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
