using LabelsMis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabelsMis.Infrastructure.Persistence.Configurations;

public class CarrierSettingsConfiguration : IEntityTypeConfiguration<CarrierSettings>
{
    public void Configure(EntityTypeBuilder<CarrierSettings> builder)
    {
        builder.ToTable("CarrierSettings");
        builder.ConfigureAuditableEntity();
        builder.Property(s => s.Environment).HasConversion<int>().IsRequired();
        builder.Property(s => s.ClientId).HasMaxLength(200).IsRequired();

        // Encrypted: base64 of nonce + tag + ciphertext, so longer than the secret itself.
        builder.Property(s => s.ClientSecretProtected).HasMaxLength(1000).IsRequired();
        builder.Property(s => s.ClientSecretTail).HasMaxLength(8).IsRequired();
        builder.Property(s => s.ShipFromContact).HasMaxLength(60);
        builder.Property(s => s.ShipFromPhone).HasMaxLength(30);
        builder.Property(s => s.DefaultServiceCode).HasMaxLength(10).IsRequired();
        builder.Property(s => s.TrackingEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.BoxPresetsJson).HasMaxLength(CarrierSettings.MaxBoxPresetsLength);
        builder.Property(s => s.LastTestMessage).HasMaxLength(CarrierSettings.MaxMessageLength);
        builder.Property(s => s.BillToAccountNumber).HasMaxLength(20);
        builder.Property(s => s.BillToPostalCode).HasMaxLength(12);

        builder.Ignore(s => s.HasCredentials);
        builder.Ignore(s => s.BoxPresets);
    }
}
