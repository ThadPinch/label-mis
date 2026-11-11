using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class QuickBooksConnectionConfiguration : IEntityTypeConfiguration<QuickBooksConnection>
{
    public void Configure(EntityTypeBuilder<QuickBooksConnection> builder)
    {
        builder.ToTable("QuickBooksConnection");
        builder.ConfigureAuditableEntity();
        builder.Property(c => c.Environment).HasConversion<int>().IsRequired();
        builder.Property(c => c.ClientId).HasMaxLength(200).IsRequired();

        // Encrypted values: base64 of nonce + tag + ciphertext, so longer than the secret itself.
        // Intuit's access tokens run to 4096 characters, hence no length cap on the token columns.
        builder.Property(c => c.ClientSecretProtected).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.ClientSecretTail).HasMaxLength(8).IsRequired();
        builder.Property(c => c.RealmId).HasMaxLength(50);
        builder.Property(c => c.CompanyName).HasMaxLength(200);
        builder.Property(c => c.AccessTokenProtected);
        builder.Property(c => c.RefreshTokenProtected);
        builder.Property(c => c.NeedsReconnect).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.LastError).HasMaxLength(QuickBooksConnection.MaxErrorLength);
        builder.Property(c => c.CompanyCheckJson).HasMaxLength(QuickBooksConnection.MaxCompanyCheckLength);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.ConnectedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(c => c.HasCredentials);
        builder.Ignore(c => c.IsLinked);
        builder.Ignore(c => c.IsConnected);
        builder.Ignore(c => c.State);
        builder.Ignore(c => c.ReconnectBy);
    }
}
