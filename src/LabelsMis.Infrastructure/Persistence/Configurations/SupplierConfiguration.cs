using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Supplier");
        builder.ConfigureMasterDataEntity();

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Terms).HasMaxLength(100).IsRequired();
        builder.Property(s => s.AccountNumber).HasMaxLength(100);
        builder.Property(s => s.IsOutsourceVendor).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.OutsourceNotes).HasMaxLength(2000);

        builder.HasIndex(s => s.Code).IsUnique();

        builder.HasMany(s => s.Contacts)
            .WithOne(c => c.Supplier)
            .HasForeignKey(c => c.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Contacts).HasField("_contacts");
    }
}
