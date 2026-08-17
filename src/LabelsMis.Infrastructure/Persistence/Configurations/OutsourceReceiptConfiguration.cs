using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class OutsourceReceiptConfiguration : IEntityTypeConfiguration<OutsourceReceipt>
{
    public void Configure(EntityTypeBuilder<OutsourceReceipt> builder)
    {
        builder.ToTable("OutsourceReceipt");
        builder.ConfigureAuditableEntity();

        builder.Property(r => r.ReceivedOn).IsRequired();
        builder.Property(r => r.Quantity).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(1000);

        builder.HasIndex(r => r.OutsourcedItemId);
    }
}
