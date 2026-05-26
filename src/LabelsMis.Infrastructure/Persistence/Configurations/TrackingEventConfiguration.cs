using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class TrackingEventConfiguration : IEntityTypeConfiguration<TrackingEvent>
{
    public void Configure(EntityTypeBuilder<TrackingEvent> builder)
    {
        builder.ToTable("TrackingEvent");
        builder.ConfigureAuditableEntity();

        builder.Property(e => e.EventAt).IsRequired();
        builder.Property(e => e.StatusDescription).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Location).HasMaxLength(200);
        builder.Property(e => e.RawPayload).HasColumnType("jsonb");

        builder.HasIndex(e => e.ShipmentPackageId);
        builder.HasIndex(e => e.EventAt);
    }
}
