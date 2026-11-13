using System.Text.Json;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>One entry of a QuickBooks list, as read from a query answer or from the cache.</summary>
public sealed record QuickBooksListEntry(
    QuickBooksCatalogKind Kind, string Id, string Name, string FullName, bool? Taxable = null);

/// <summary>Reads QuickBooks' answers to the list queries (<c>select * from Item</c>, Class, Term).</summary>
public static class QuickBooksCatalogParser
{
    /// <summary>The entries in one page of a query answer, and how many rows the page held (which
    /// is what decides whether to ask for another page). Inactive entries and item categories are
    /// left out: neither can go on an invoice line.</summary>
    public static (IReadOnlyList<QuickBooksListEntry> Entries, int RowCount) ParsePage(QuickBooksCatalogKind kind, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("QueryResponse", out var response)
            || !response.TryGetProperty(kind.ToString(), out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return ([], 0);
        }

        var entries = new List<QuickBooksListEntry>();
        var count = 0;
        foreach (var row in rows.EnumerateArray())
        {
            count++;
            var id = Text(row, "Id");
            var name = Text(row, "Name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (row.TryGetProperty("Active", out var active) && active.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            if (kind == QuickBooksCatalogKind.Item
                && string.Equals(Text(row, "Type"), "Category", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool? taxable = kind == QuickBooksCatalogKind.Item
                ? row.TryGetProperty("Taxable", out var t) && t.ValueKind == JsonValueKind.True
                : null;
            entries.Add(new QuickBooksListEntry(kind, id!, name!, Text(row, "FullyQualifiedName") ?? name!, taxable));
        }

        return (entries, count);
    }

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>Turns the names LabelShop's mapping holds into QuickBooks entries. A name matches an
/// entry's full name ("Design:Fountains:Pump") first, then its own name ("Pump") when exactly one
/// entry has it. Case and outer spaces are ignored: QuickBooks itself treats names that way.</summary>
public sealed class QuickBooksCatalogLookup
{
    private readonly IReadOnlyList<QuickBooksListEntry> _entries;

    public QuickBooksCatalogLookup(IEnumerable<QuickBooksListEntry> entries)
    {
        _entries = entries.ToList();
    }

    public static QuickBooksCatalogLookup Empty { get; } = new([]);

    public bool HasAny(QuickBooksCatalogKind kind) => _entries.Any(e => e.Kind == kind);

    /// <summary>The names to offer in a dropdown, sorted.</summary>
    public IReadOnlyList<string> Names(QuickBooksCatalogKind kind) =>
        _entries.Where(e => e.Kind == kind)
            .Select(e => e.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public QuickBooksListEntry? Find(QuickBooksCatalogKind kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var wanted = name.Trim();
        var ofKind = _entries.Where(e => e.Kind == kind).ToList();
        var byFullName = ofKind.FirstOrDefault(e => string.Equals(e.FullName, wanted, StringComparison.OrdinalIgnoreCase));
        if (byFullName is not null)
        {
            return byFullName;
        }

        var byName = ofKind.Where(e => string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }
}
