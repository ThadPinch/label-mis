using LabelsMis.Domain.Common;

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
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (taxRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxRate), "Tax rate must be between 0 and 1.");
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
