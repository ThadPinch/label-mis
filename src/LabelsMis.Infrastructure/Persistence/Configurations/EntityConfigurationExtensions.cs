using LabelsMis.Domain.Common;
using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

internal static class EntityConfigurationExtensions
{
    public static void ConfigureAuditableEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : EntityBase
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId)
            .IsRequired()
            .HasDefaultValue(TenantConstants.DefaultTenantId);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.CreatedById)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.ModifiedById)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public static void ConfigureMasterDataEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : MasterDataEntity
    {
        builder.ConfigureAuditableEntity();
        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
    }

    public static PropertyBuilder<decimal> HasMoneyPrecision(this PropertyBuilder<decimal> builder) =>
        builder.HasPrecision(18, 4);

    public static PropertyBuilder<decimal> HasDimensionPrecision(this PropertyBuilder<decimal> builder) =>
        builder.HasPrecision(10, 4);

    public static PropertyBuilder<decimal> HasQuantityPrecision(this PropertyBuilder<decimal> builder) =>
        builder.HasPrecision(14, 4);
}
