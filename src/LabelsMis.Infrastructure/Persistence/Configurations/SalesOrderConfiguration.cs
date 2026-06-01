using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("SalesOrder");
        builder.ConfigureAuditableEntity();

        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.CustomerPoNumber).HasMaxLength(100);
        builder.Property(o => o.OrderedAt).IsRequired();
        builder.Property(o => o.Status).IsRequired();
        builder.Property(o => o.Notes).HasMaxLength(4000);

        builder.Property(o => o.ShippingCost).HasMoneyPrecision();
        builder.Property(o => o.ShipToName).HasMaxLength(200);
        builder.Property(o => o.ShipToStreet1).HasMaxLength(200);
        builder.Property(o => o.ShipToStreet2).HasMaxLength(200);
        builder.Property(o => o.ShipToCity).HasMaxLength(100);
        builder.Property(o => o.ShipToState).HasMaxLength(100);
        builder.Property(o => o.ShipToZip).HasMaxLength(20);
        builder.Property(o => o.ShipToCountry).HasMaxLength(2);
        builder.Ignore(o => o.ShippingAddress);

        builder.HasOne(o => o.ShippingMethod)
            .WithMany()
            .HasForeignKey(o => o.ShippingMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.CustomerId);
        builder.HasIndex(o => o.SourceEstimateId);

        builder.HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.SourceEstimate)
            .WithMany()
            .HasForeignKey(o => o.SourceEstimateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.Lines)
            .WithOne(l => l.SalesOrder)
            .HasForeignKey(l => l.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Lines).HasField("_lines");
    }
}
