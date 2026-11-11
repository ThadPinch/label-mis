using System.Text.Json;

namespace LabelsMis.Domain.QuickBooks;

public static class QuickBooksCompanyInfo
{
    /// <summary>The company's name from the JSON of <c>GET /companyinfo/{id}</c>, or null when the
    /// answer has none.</summary>
    public static string? ReadCompanyName(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("CompanyInfo", out var info)
            || info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var field in (string[])["CompanyName", "LegalName"])
        {
            if (info.TryGetProperty(field, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!.Trim();
            }
        }

        return null;
    }
}
