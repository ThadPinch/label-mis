using LabelsMis.Domain.Common;
using LabelsMis.Domain.Shipping;

namespace LabelsMis.Domain.Entities;

public class GeneralSettings : EntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private GeneralSettings()
    {
    }

    public string CompanyName { get; private set; } = string.Empty;
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? Zip { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public string? TermsText { get; private set; }

    /// <summary>Sales tax rate as a fraction (0.0825 = 8.25%), applied to new invoices for
    /// taxable customers.</summary>
    public decimal TaxRate { get; private set; } = 0.0825m;

    /// <summary>Shop-wide default markup as a fraction (0.55 = 55%). Seeds the markup on new
    /// customers and is the last fallback when neither a line override, the product, nor the
    /// customer supplies one.</summary>
    public decimal DefaultMarkupPct { get; private set; } = DefaultMarkupPctFallback;

    public const decimal DefaultMarkupPctFallback = 0.55m;

    /// <summary>Flat shipping billed for a local delivery.</summary>
    public decimal LocalDeliveryFee { get; private set; } = ShippingBillingSettings.DefaultLocalDeliveryFee;

    /// <summary>Carrier shipments bill the carrier's actual cost × this (1.5 = cost plus half).</summary>
    public decimal CarrierCostMultiplier { get; private set; } = ShippingBillingSettings.DefaultCarrierCostMultiplier;

    public ShippingBillingSettings ShippingBilling => new(LocalDeliveryFee, CarrierCostMultiplier);

    public byte[]? LogoBytes { get; private set; }
    public string? LogoContentType { get; private set; }

    public static GeneralSettings CreateDefault(Guid createdById, DateTime createdAt)
    {
        var settings = new GeneralSettings();
        settings.SetCreated(SingletonId, createdById, createdAt);
        return settings;
    }

    public void Update(
        string companyName,
        string? addressLine1,
        string? addressLine2,
        string? city,
        string? state,
        string? zip,
        string? phone,
        string? email,
        string? website,
        string? termsText,
        decimal taxRate,
        decimal defaultMarkupPct,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (taxRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxRate), "Tax rate must be between 0 and 1.");
        }

        if (defaultMarkupPct is < 0m or > 10m)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultMarkupPct), "Default markup must be between 0 and 10 (0%–1000%).");
        }

        CompanyName = companyName.Trim();
        AddressLine1 = Normalize(addressLine1);
        AddressLine2 = Normalize(addressLine2);
        City = Normalize(city);
        State = Normalize(state);
        Zip = Normalize(zip);
        Phone = Normalize(phone);
        Email = Normalize(email);
        Website = Normalize(website);
        TermsText = Normalize(termsText);
        TaxRate = taxRate;
        DefaultMarkupPct = defaultMarkupPct;
        SetModified(modifiedById, modifiedAt);
    }

    public void UpdateShippingBilling(
        decimal localDeliveryFee,
        decimal carrierCostMultiplier,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (localDeliveryFee < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(localDeliveryFee), "Local delivery fee cannot be negative.");
        }

        if (carrierCostMultiplier is < 0m or > 10m)
        {
            throw new ArgumentOutOfRangeException(nameof(carrierCostMultiplier), "Carrier cost multiplier must be between 0 and 10.");
        }

        LocalDeliveryFee = localDeliveryFee;
        CarrierCostMultiplier = carrierCostMultiplier;
        SetModified(modifiedById, modifiedAt);
    }

    public void SetLogo(byte[] logoBytes, string contentType, Guid modifiedById, DateTime modifiedAt)
    {
        LogoBytes = logoBytes;
        LogoContentType = contentType;
        SetModified(modifiedById, modifiedAt);
    }

    public void ClearLogo(Guid modifiedById, DateTime modifiedAt)
    {
        LogoBytes = null;
        LogoContentType = null;
        SetModified(modifiedById, modifiedAt);
    }

    public bool HasLogo => LogoBytes is { Length: > 0 };

    /// <summary>Formats "City, State Zip", omitting any missing parts.</summary>
    public string? CityStateZip
    {
        get
        {
            var cityState = string.Join(", ", new[] { City, State }.Where(p => !string.IsNullOrWhiteSpace(p)));
            var line = string.Join(" ", new[] { cityState, Zip }.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
