using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class ProductCustomerConfiguration : IEntityTypeConfiguration<ProductCustomer>
{
    public void Configure(EntityTypeBuilder<ProductCustomer> builder)
    {
        builder.ToTable("ProductCustomer");
        builder.ConfigureAuditableEntity();

        builder.HasIndex(pc => pc.CustomerId);
        builder.HasIndex(pc => new { pc.ProductId, pc.CustomerId }).IsUnique();

        builder.HasOne(pc => pc.Product)
            .WithMany(p => p.CustomerAssignments)
            .HasForeignKey(pc => pc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pc => pc.Customer)
            .WithMany()
            .HasForeignKey(pc => pc.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
