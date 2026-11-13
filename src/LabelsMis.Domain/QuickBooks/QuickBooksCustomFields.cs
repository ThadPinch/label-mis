using System.Text.Json;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>A QuickBooks custom field an invoice can carry, as "Find the PO field" found it.
/// <see cref="DefinitionId"/> is what an invoice refers to it by. <see cref="InvoiceCount"/> is how
/// many of the invoices read carried it; a legacy field listed in Preferences has 0.</summary>
public sealed record QuickBooksCustomFieldDefinition(string DefinitionId, string Name, int InvoiceCount, bool Legacy)
{
    /// <summary>Named like a PO number ("P.O. Number", "PO #", "Purchase Order").</summary>
    public bool LooksLikePoNumber => QuickBooksCompanyCheck.LooksLikePoNumber(Name);
}

/// <summary>Finds the custom fields a company's invoices use. A field made in QuickBooks' current
/// Custom fields screen has no list of its own that the free API tier can read, and Preferences
/// doesn't report it; it shows up only on an invoice read with
/// <c>include=enhancedAllCustomFields</c>, and only on an invoice that has a value in it.</summary>
public static class QuickBooksCustomFieldParser
{
    /// <summary>The fields on the invoices in a query answer (<c>QueryResponse.Invoice[]</c>) or a
    /// single read (<c>Invoice</c>), and how many invoices the answer held. Only a field that came
    /// back with its name counts: a value written to a legacy id ("1" to "3") comes back nameless
    /// and isn't printed on the invoice. Most-used first, PO-like names ahead of the rest.</summary>
    public static (IReadOnlyList<QuickBooksCustomFieldDefinition> Fields, int InvoiceCount) FromInvoices(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var invoices = new List<JsonElement>();
        if (root.TryGetProperty("QueryResponse", out var response)
            && response.ValueKind == JsonValueKind.Object
            && response.TryGetProperty("Invoice", out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            invoices.AddRange(rows.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object));
        }
        else if (root.TryGetProperty("Invoice", out var single) && single.ValueKind == JsonValueKind.Object)
        {
            invoices.Add(single);
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var invoice in invoices)
        {
            if (!invoice.TryGetProperty("CustomField", out var fields) || fields.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var seenOnThisInvoice = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields.EnumerateArray())
            {
                var id = Text(field, "DefinitionId")?.Trim();
                var name = Text(field, "Name")?.Trim();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || !seenOnThisInvoice.Add(id))
                {
                    continue;
                }

                // The newest invoice comes first in the recent-invoices query: its name wins.
                names.TryAdd(id, name);
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        var found = names.Select(pair => new QuickBooksCustomFieldDefinition(pair.Key, pair.Value, counts[pair.Key], Legacy: false));
        return (Order(found), invoices.Count);
    }

    /// <summary>The legacy text fields switched on in <c>GET /preferences</c>
    /// (<c>SalesFormsPrefs.CustomField</c>). Their ids are "1" to "3".</summary>
    public static IReadOnlyList<QuickBooksCustomFieldDefinition> FromPreferences(string json) =>
        Order(QuickBooksCompanyCheck.FromPreferences(json).CustomFields
            .Where(f => f.Enabled)
            .Select(f => new QuickBooksCustomFieldDefinition(f.DefinitionId, f.Name, 0, Legacy: true)));

    /// <summary>The field to offer first: the only one named like a PO number, else null.</summary>
    public static QuickBooksCustomFieldDefinition? Suggest(IEnumerable<QuickBooksCustomFieldDefinition> fields)
    {
        var poLike = fields.Where(f => f.LooksLikePoNumber).ToList();
        return poLike.Count == 1 ? poLike[0] : null;
    }

    private static IReadOnlyList<QuickBooksCustomFieldDefinition> Order(IEnumerable<QuickBooksCustomFieldDefinition> fields) =>
        fields
            .OrderByDescending(f => f.LooksLikePoNumber)
            .ThenByDescending(f => f.InvoiceCount)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.DefinitionId, StringComparer.Ordinal)
            .ToList();

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
