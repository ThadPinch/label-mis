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
        builder.Property(s => s.CarrierCost).HasPrecision(18, 4);
        builder.Property(s => s.BilledShipping).HasPrecision(18, 4);

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

        // A label bought through LabelShop: what the carrier was asked and what it answered.
        builder.Property(s => s.ShipToContact).HasMaxLength(100);
        builder.Property(s => s.ShipToPhone).HasMaxLength(30);
        builder.Property(s => s.LabelState).IsRequired();
        builder.Property(s => s.CarrierQuoteId).HasMaxLength(100);
        builder.Property(s => s.CarrierOfferId).HasMaxLength(100);
        builder.Property(s => s.CarrierOrderNumber).HasMaxLength(50);
        builder.Property(s => s.CarrierServiceCode).HasMaxLength(10);
        builder.Property(s => s.CarrierServiceName).HasMaxLength(100);
        builder.Property(s => s.CarrierBillToAccount).HasMaxLength(20);
        builder.Property(s => s.CarrierBillToPostalCode).HasMaxLength(12);
        builder.Property(s => s.VoidReason).HasMaxLength(Shipment.MaxVoidReasonLength);
        builder.Ignore(s => s.IsSampleLabel);
        builder.Ignore(s => s.CarrierBillTo);
        builder.Ignore(s => s.CountsAsShipped);

        // A carrier quote is booked once: two shipments can never hold the same one.
        builder.HasIndex(s => s.CarrierQuoteId)
            .IsUnique()
            .HasFilter("\"CarrierQuoteId\" IS NOT NULL");

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
