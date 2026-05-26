using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class EstimateConfiguration : IEntityTypeConfiguration<Estimate>
{
    public void Configure(EntityTypeBuilder<Estimate> builder)
    {
        builder.ToTable("Estimate");
        builder.ConfigureAuditableEntity();

        builder.Property(e => e.EstimateNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.RevisionNumber).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(4000);
        builder.Property(e => e.LostReason).HasMaxLength(500);
        builder.Property(e => e.PdfFilePath).HasMaxLength(500);

        builder.HasIndex(e => e.EstimateNumber);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CustomerId);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Estimate)
            .HasForeignKey(l => l.EstimateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Revisions)
            .WithOne(r => r.Estimate)
            .HasForeignKey(r => r.EstimateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Lines).HasField("_lines");
        builder.Navigation(e => e.Revisions).HasField("_revisions");
    }
}
