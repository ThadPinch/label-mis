using LabelsMis.Domain.Entities;

namespace LabelsMis.Web.Pages.Legal;

/// <summary>Who runs this LabelShop site, as the public pages name them. Taken from General
/// settings, so the terms and the privacy policy name the company the site is set up for and
/// never carry a company name of their own.</summary>
public sealed record SiteOperator(string Name, bool IsNamed, string? Email, string? PostalAddress)
{
    public const string UnnamedOperator = "the operator of this site";

    public static SiteOperator Unnamed { get; } = new(UnnamedOperator, false, null, null);

    /// <summary>The name as it reads at the start of a sentence.</summary>
    public string SentenceStart => IsNamed ? Name : char.ToUpperInvariant(Name[0]) + Name[1..];

    public static SiteOperator From(GeneralSettings? settings)
    {
        if (settings is null)
        {
            return Unnamed;
        }

        var name = Clean(settings.CompanyName);
        return new SiteOperator(name ?? UnnamedOperator, name is not null, Clean(settings.Email), Address(settings));
    }

    private static string? Address(GeneralSettings settings)
    {
        var cityLine = string.Join(
            " ",
            new[] { string.Join(", ", new[] { Clean(settings.City), Clean(settings.State) }.Where(part => part is not null)), Clean(settings.Zip) }
                .Where(part => !string.IsNullOrEmpty(part)));
        var lines = new[] { Clean(settings.AddressLine1), Clean(settings.AddressLine2), Clean(cityLine) }
            .Where(line => line is not null)
            .ToList();
        return lines.Count == 0 ? null : string.Join(", ", lines);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
