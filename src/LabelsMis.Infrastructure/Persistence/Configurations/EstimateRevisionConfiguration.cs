using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class EstimateRevisionConfiguration : IEntityTypeConfiguration<EstimateRevision>
{
    public void Configure(EntityTypeBuilder<EstimateRevision> builder)
    {
        builder.ToTable("EstimateRevision");
        builder.ConfigureAuditableEntity();

        builder.Property(r => r.RevisionNumber).IsRequired();
        builder.Property(r => r.SnapshotJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(r => new { r.EstimateId, r.RevisionNumber }).IsUnique();
    }
}
