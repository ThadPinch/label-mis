using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {
        builder.ToTable("ShipmentLine");
        builder.ConfigureAuditableEntity();

        builder.Property(l => l.QuantityShipped).IsRequired();

        builder.HasIndex(l => l.ShipmentId);
        builder.HasIndex(l => l.SalesOrderLineId);
        builder.HasIndex(l => l.JobId);

        builder.HasOne(l => l.SalesOrderLine)
            .WithMany()
            .HasForeignKey(l => l.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Job)
            .WithMany()
            .HasForeignKey(l => l.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
