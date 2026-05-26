using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipt");
        builder.ConfigureAuditableEntity();

        builder.Property(r => r.ReceivedAt).IsRequired();
        builder.Property(r => r.QuantityLf).HasQuantityPrecision();
        builder.Property(r => r.Notes).HasMaxLength(1000);

        builder.HasIndex(r => r.PoLineId);
    }
}
