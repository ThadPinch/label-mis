using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("DocumentSequence");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DocumentType).HasMaxLength(10).IsRequired();
        builder.Property(d => d.Year).IsRequired();
        builder.Property(d => d.LastNumber).IsRequired();

        builder.HasIndex(d => new { d.DocumentType, d.Year }).IsUnique();
    }
}
