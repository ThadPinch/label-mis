using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class RollMovementConfiguration : IEntityTypeConfiguration<RollMovement>
{
    public void Configure(EntityTypeBuilder<RollMovement> builder)
    {
        builder.ToTable("RollMovement");
        builder.ConfigureAuditableEntity();

        builder.Property(m => m.MovementType).IsRequired();
        builder.Property(m => m.QuantityLf).HasQuantityPrecision();
        builder.Property(m => m.MovedAt).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.HasIndex(m => m.RollId);
        builder.HasIndex(m => m.JobId);

        builder.HasOne(m => m.Job)
            .WithMany()
            .HasForeignKey(m => m.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
