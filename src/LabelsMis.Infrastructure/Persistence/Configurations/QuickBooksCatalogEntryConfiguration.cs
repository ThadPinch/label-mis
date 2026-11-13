using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class QuickBooksCatalogEntryConfiguration : IEntityTypeConfiguration<QuickBooksCatalogEntry>
{
    public void Configure(EntityTypeBuilder<QuickBooksCatalogEntry> builder)
    {
        builder.ToTable("QuickBooksCatalogEntry");
        builder.ConfigureAuditableEntity();
        builder.Property(e => e.Kind).IsRequired();
        builder.Property(e => e.QuickBooksId).HasMaxLength(QuickBooksCatalogEntry.MaxIdLength).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(QuickBooksCatalogEntry.MaxNameLength).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(QuickBooksCatalogEntry.MaxFullNameLength).IsRequired();
        builder.Property(e => e.RefreshedAt).IsRequired();

        // One row per QuickBooks entry: a refresh updates it in place.
        builder.HasIndex(e => new { e.Kind, e.QuickBooksId }).IsUnique();
    }
}
