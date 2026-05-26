using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoice");
        builder.ConfigureAuditableEntity();

        builder.Property(i => i.InvoiceNumber).HasMaxLength(20).IsRequired();
        builder.Property(i => i.InvoiceDate).IsRequired();
        builder.Property(i => i.DueDate).IsRequired();
        builder.Property(i => i.Status).IsRequired();
        builder.Property(i => i.Subtotal).HasMoneyPrecision();
        builder.Property(i => i.TaxAmount).HasMoneyPrecision();
        builder.Property(i => i.ShippingAmount).HasMoneyPrecision();
        builder.Property(i => i.Total).HasMoneyPrecision();
        builder.Property(i => i.BalanceDue).HasMoneyPrecision();
        builder.Property(i => i.QbInvoiceId).HasMaxLength(100);
        builder.Property(i => i.Notes).HasMaxLength(4000);
        builder.Property(i => i.PdfFilePath).HasMaxLength(500);
        builder.Property(i => i.VoidReason).HasMaxLength(500);

        builder.HasIndex(i => i.InvoiceNumber).IsUnique();
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.CustomerId);
        builder.HasIndex(i => i.SalesOrderId);

        builder.HasOne(i => i.Customer)
            .WithMany()
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.SalesOrder)
            .WithMany()
            .HasForeignKey(i => i.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Shipment)
            .WithMany()
            .HasForeignKey(i => i.ShipmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(i => i.Lines)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Payments)
            .WithOne(p => p.Invoice)
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Lines).HasField("_lines");
        builder.Navigation(i => i.Payments).HasField("_payments");
    }
}
