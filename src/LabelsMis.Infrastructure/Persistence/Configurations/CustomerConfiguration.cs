using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customer");
        builder.ConfigureMasterDataEntity();

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Terms).IsRequired();
        builder.Property(c => c.DefaultMarkupPct).HasMoneyPrecision();
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(2000);

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.Name);

        builder.HasMany(c => c.Addresses)
            .WithOne(a => a.Customer)
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Contacts)
            .WithOne(c => c.Customer)
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Addresses).HasField("_addresses");
        builder.Navigation(c => c.Contacts).HasField("_contacts");
    }
}
