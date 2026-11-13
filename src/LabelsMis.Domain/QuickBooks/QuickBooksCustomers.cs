using System.Text.Json;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>A QuickBooks customer as the customer pages show it: enough to tell two of them apart
/// and to link one. <see cref="IsSubCustomer"/> marks a sub-customer (QuickBooks calls them jobs),
/// whose <see cref="FullyQualifiedName"/> carries the parent ("Parent:Child").</summary>
public sealed record QuickBooksCustomerSummary(
    string Id,
    string DisplayName,
    string? CompanyName = null,
    string? FullyQualifiedName = null,
    bool IsSubCustomer = false,
    bool Active = true,
    string? Email = null,
    string? Phone = null,
    string? Place = null);

/// <summary>Reads QuickBooks' customer answers: a query page (<c>QueryResponse.Customer[]</c>)
/// or one customer (<c>{"Customer":{…}}</c>, the answer to a read or a create).</summary>
public static class QuickBooksCustomerParser
{
    /// <summary>The customers in one page of a query answer, and how many rows the page held
    /// (which decides whether to ask for another page).</summary>
    public static (IReadOnlyList<QuickBooksCustomerSummary> Customers, int RowCount) ParsePage(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("QueryResponse", out var response)
            || response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("Customer", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return ([], 0);
        }

        var customers = new List<QuickBooksCustomerSummary>();
        var count = 0;
        foreach (var row in rows.EnumerateArray())
        {
            count++;
            if (Read(row) is { } customer)
            {
                customers.Add(customer);
            }
        }

        return (customers, count);
    }

    /// <summary>The customer in a read or create answer; null when the answer holds none.</summary>
    public static QuickBooksCustomerSummary? ParseOne(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
               && document.RootElement.TryGetProperty("Customer", out var customer)
            ? Read(customer)
            : null;
    }

    private static QuickBooksCustomerSummary? Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = Text(row, "Id");
        var displayName = Text(row, "DisplayName");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var isSub = (row.TryGetProperty("Job", out var job) && job.ValueKind == JsonValueKind.True)
                    || (row.TryGetProperty("ParentRef", out var parent) && parent.ValueKind == JsonValueKind.Object);
        var active = !(row.TryGetProperty("Active", out var a) && a.ValueKind == JsonValueKind.False);

        string? place = null;
        if (row.TryGetProperty("BillAddr", out var bill) && bill.ValueKind == JsonValueKind.Object)
        {
            var parts = new[] { Text(bill, "City"), Text(bill, "CountrySubDivisionCode") }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .ToList();
            place = parts.Count == 0 ? null : string.Join(", ", parts);
        }

        return new QuickBooksCustomerSummary(
            id!.Trim(),
            displayName!.Trim(),
            Blank(Text(row, "CompanyName")),
            Blank(Text(row, "FullyQualifiedName")),
            isSub,
            active,
            Blank(Nested(row, "PrimaryEmailAddr", "Address")),
            Blank(Nested(row, "PrimaryPhone", "FreeFormNumber")),
            place);
    }

    private static string? Nested(JsonElement row, string outer, string inner) =>
        row.TryGetProperty(outer, out var value) && value.ValueKind == JsonValueKind.Object ? Text(value, inner) : null;

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The QuickBooks queries the customer pages run. Text in a literal has its quotes escaped
/// (<c>\'</c>), as QuickBooks' query language wants.</summary>
public static class QuickBooksCustomerQueries
{
    /// <summary>The most a search shows.</summary>
    public const int SearchLimit = 20;

    /// <summary>QuickBooks' largest page.</summary>
    public const int PageSize = 1000;

    /// <summary>Active customers whose display name contains <paramref name="text"/>. Null when
    /// there is nothing to search for.</summary>
    public static string? Search(string? text)
    {
        var cleaned = CleanSearchText(text);
        return cleaned is null
            ? null
            : $"select * from Customer where Active = true and DisplayName like '%{Escape(cleaned)}%' startposition 1 maxresults {SearchLimit}";
    }

    /// <summary>One page of the active customers; <paramref name="startPosition"/> counts from 1.</summary>
    public static string ActivePage(int startPosition) =>
        $"select * from Customer where Active = true startposition {Math.Max(1, startPosition)} maxresults {PageSize}";

    /// <summary>The active customer with exactly this display name (QuickBooks keeps them unique).</summary>
    public static string ByDisplayName(string displayName) =>
        $"select * from Customer where DisplayName = '{Escape(displayName.Trim())}'";

    /// <summary>The search text as it goes into the query: trimmed, without the <c>%</c> wildcard,
    /// at most 100 characters. Null when nothing is left.</summary>
    public static string? CleanSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        if (cleaned.Length > 100)
        {
            cleaned = cleaned[..100].Trim();
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    public static string Escape(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);
}

/// <summary>The names the activity log files the customer calls under (see <see cref="QuickBooksOperations"/>).</summary>
public static class QuickBooksCustomerOperations
{
    public const string Search = "customer.search";
    public const string Read = "customer.read";
    public const string Create = "customer.create";
    public const string List = "customer.list";

    /// <summary>What the log page calls an operation; null for one that isn't a customer call.</summary>
    public static string? Describe(string operation) => operation switch
    {
        Search => "Search QuickBooks customers",
        Read => "Read a QuickBooks customer",
        Create => "Create a customer in QuickBooks",
        List => "Read the QuickBooks customer list",
        _ => null
    };
}
