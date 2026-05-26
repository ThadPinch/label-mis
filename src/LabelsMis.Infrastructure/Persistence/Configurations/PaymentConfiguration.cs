using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payment");
        builder.ConfigureAuditableEntity();

        builder.Property(p => p.PaymentDate).IsRequired();
        builder.Property(p => p.Amount).HasMoneyPrecision();
        builder.Property(p => p.Method).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.HasIndex(p => p.InvoiceId);
        builder.HasIndex(p => p.PaymentDate);
    }
}
