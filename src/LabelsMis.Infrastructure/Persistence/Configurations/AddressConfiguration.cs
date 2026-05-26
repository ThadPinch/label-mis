using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Address");
        builder.ConfigureAuditableEntity();

        builder.Property(a => a.Street1).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Street2).HasMaxLength(200);
        builder.Property(a => a.City).HasMaxLength(100).IsRequired();
        builder.Property(a => a.State).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Zip).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Country).HasMaxLength(2).IsRequired();
        builder.Property(a => a.AddressType).IsRequired();
    }
}
