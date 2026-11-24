using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class CarrierLogConfiguration : IEntityTypeConfiguration<CarrierLog>
{
    public void Configure(EntityTypeBuilder<CarrierLog> builder)
    {
        builder.ToTable("CarrierLog");
        builder.ConfigureAuditableEntity();
        builder.Property(l => l.Operation).HasMaxLength(CarrierLog.MaxOperationLength).IsRequired();
        builder.Property(l => l.Succeeded).IsRequired();
        builder.Property(l => l.Automatic).IsRequired().HasDefaultValue(false);
        builder.Property(l => l.Environment).HasConversion<int>().IsRequired();
        builder.Property(l => l.Detail).HasMaxLength(CarrierLog.MaxDetailLength);
        builder.Property(l => l.ErrorKind).HasMaxLength(30);
        builder.Property(l => l.Message).HasMaxLength(CarrierLog.MaxMessageLength);
        builder.Property(l => l.CorrelationId).HasMaxLength(100);

        // No foreign keys to Shipment or SalesOrder: the log must outlive whatever it describes
        // (an unanswered purchase that turns out never to have happened is deleted; its log rows stay).
        builder.HasIndex(l => l.CreatedAt);
        builder.HasIndex(l => l.ShipmentId);
    }
}
