using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class StockCostHistoryConfiguration : IEntityTypeConfiguration<StockCostHistory>
{
    public void Configure(EntityTypeBuilder<StockCostHistory> builder)
    {
        builder.ToTable("StockCostHistory");
        builder.ConfigureAuditableEntity();

        builder.Property(h => h.CostPerMsi).HasMoneyPrecision();
        builder.Property(h => h.EffectiveDate).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(h => h.RecordedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
