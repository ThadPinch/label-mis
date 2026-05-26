using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Product");
        builder.ConfigureMasterDataEntity();

        builder.Property(p => p.CustomerSku).HasMaxLength(100);
        builder.Property(p => p.InternalSku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500).IsRequired();
        builder.Property(p => p.LabelAcrossIn).HasDimensionPrecision();
        builder.Property(p => p.LabelAroundIn).HasDimensionPrecision();
        builder.Property(p => p.CornerRadiusIn).HasDimensionPrecision();
        builder.Property(p => p.InkSet).IsRequired();
        builder.Property(p => p.FinishingOperationsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.ArtworkFilePath).HasMaxLength(500);
        builder.Property(p => p.Status).IsRequired();

        builder.HasIndex(p => p.InternalSku).IsUnique();
        builder.HasIndex(p => new { p.PrimaryCustomerId, p.InternalSku });
        builder.HasIndex(p => p.SourceEstimateLineId);

        builder.HasOne(p => p.PrimaryCustomer)
            .WithMany()
            .HasForeignKey(p => p.PrimaryCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.SourceEstimateLine)
            .WithMany()
            .HasForeignKey(p => p.SourceEstimateLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Substrate)
            .WithMany()
            .HasForeignKey(p => p.SubstrateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Die)
            .WithMany()
            .HasForeignKey(p => p.DieId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.RollSpec)
            .WithOne(r => r.Product)
            .HasForeignKey<RollSpec>(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.CustomerAssignments).HasField("_customerAssignments");
        builder.Navigation(p => p.RollSpec).HasField("_rollSpec");
    }
}
