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
        builder.Property(e => e.BillingNotes).HasMaxLength(4000);
        builder.Property(e => e.LostReason).HasMaxLength(500);
        builder.Property(e => e.PdfFilePath).HasMaxLength(500);
        builder.Property(e => e.SentAt);
        builder.Property(e => e.ContactEmail).HasMaxLength(256);

        builder.Property(e => e.ShippingCost).HasMoneyPrecision();
        builder.Property(e => e.ShipToName).HasMaxLength(200);
        builder.Property(e => e.ShipToStreet1).HasMaxLength(200);
        builder.Property(e => e.ShipToStreet2).HasMaxLength(200);
        builder.Property(e => e.ShipToCity).HasMaxLength(100);
        builder.Property(e => e.ShipToState).HasMaxLength(100);
        builder.Property(e => e.ShipToZip).HasMaxLength(20);
        builder.Property(e => e.ShipToCountry).HasMaxLength(2);
        builder.Ignore(e => e.ShippingAddress);

        builder.HasOne(e => e.ShippingMethod)
            .WithMany()
            .HasForeignKey(e => e.ShippingMethodId)
            .OnDelete(DeleteBehavior.Restrict);

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
        builder.Navigation(e => e.Charges).HasField("_charges");
        builder.Navigation(e => e.Revisions).HasField("_revisions");
    }
}
