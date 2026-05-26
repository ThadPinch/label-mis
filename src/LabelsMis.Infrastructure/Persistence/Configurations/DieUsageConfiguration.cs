using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class DieUsageConfiguration : IEntityTypeConfiguration<DieUsage>
{
    public void Configure(EntityTypeBuilder<DieUsage> builder)
    {
        builder.ToTable("DieUsage");
        builder.ConfigureAuditableEntity();

        builder.Property(u => u.UsedAt).IsRequired();
        builder.Property(u => u.Notes).HasMaxLength(500);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(u => u.UsedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
