using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipment");
        builder.ConfigureAuditableEntity();

        builder.Property(s => s.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ShipDate).IsRequired();
        builder.Property(s => s.Carrier).IsRequired();
        builder.Property(s => s.Status).IsRequired();
        builder.Property(s => s.TotalDeclaredValue).HasMoneyPrecision();
        builder.Property(s => s.BillingType).IsRequired();
        builder.Property(s => s.BillingAccountNumber).HasMaxLength(50);
        builder.Property(s => s.TotalShippingCost).HasMoneyPrecision();

        builder.Property(s => s.ShipFromName).HasMaxLength(200);
        builder.Property(s => s.ShipFromStreet1).HasMaxLength(200);
        builder.Property(s => s.ShipFromStreet2).HasMaxLength(200);
        builder.Property(s => s.ShipFromCity).HasMaxLength(100);
        builder.Property(s => s.ShipFromState).HasMaxLength(50);
        builder.Property(s => s.ShipFromZip).HasMaxLength(20);
        builder.Property(s => s.ShipFromCountry).HasMaxLength(2);
        builder.Ignore(s => s.ShipFromSnapshot);

        builder.Property(s => s.ShipToName).HasMaxLength(200);
        builder.Property(s => s.ShipToStreet1).HasMaxLength(200);
        builder.Property(s => s.ShipToStreet2).HasMaxLength(200);
        builder.Property(s => s.ShipToCity).HasMaxLength(100);
        builder.Property(s => s.ShipToState).HasMaxLength(50);
        builder.Property(s => s.ShipToZip).HasMaxLength(20);
        builder.Property(s => s.ShipToCountry).HasMaxLength(2);
        builder.Ignore(s => s.ShipToSnapshot);

        builder.HasIndex(s => s.ShipmentNumber).IsUnique();
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.SalesOrderId);

        builder.HasOne(s => s.SalesOrder)
            .WithMany()
            .HasForeignKey(s => s.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ShipFromAddress)
            .WithMany()
            .HasForeignKey(s => s.ShipFromAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ShipToAddress)
            .WithMany()
            .HasForeignKey(s => s.ShipToAddressId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Packages)
            .WithOne(p => p.Shipment)
            .HasForeignKey(p => p.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Lines)
            .WithOne(l => l.Shipment)
            .HasForeignKey(l => l.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Packages).HasField("_packages");
        builder.Navigation(s => s.Lines).HasField("_lines");
    }
}
