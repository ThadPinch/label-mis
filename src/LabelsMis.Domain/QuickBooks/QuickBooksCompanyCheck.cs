using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>One of the three text custom fields QuickBooks lets the Accounting API reach on sales
/// forms. <see cref="DefinitionId"/> ("1" to "3") is what an invoice refers to it by.</summary>
public sealed record QuickBooksCustomFieldSlot(string DefinitionId, string Name, bool Enabled);

/// <summary>The company settings an invoice push depends on, read from QuickBooks' Preferences.
/// Nothing here can be fixed from LabelShop: each is a switch in the QuickBooks screens.</summary>
public sealed partial record QuickBooksCompanyCheck(
    bool CustomTransactionNumbers,
    bool ClassTrackingPerLine,
    bool ClassTrackingPerTransaction,
    bool SalesTaxOn,
    bool AutomatedSalesTax,
    IReadOnlyList<QuickBooksCustomFieldSlot> CustomFields)
{
    private static readonly JsonSerializerOptions StoredJson = new(JsonSerializerDefaults.Web);

    /// <summary>The custom field that holds the customer's PO number, when one is switched on and
    /// named like one ("P.O. Number", "PO #", "Purchase Order"). Null does not mean the company has
    /// none: Preferences lists only the three legacy fields, and a field made in QuickBooks' current
    /// Custom fields screen is not reported there (seen on the sandbox company).</summary>
    [JsonIgnore]
    public QuickBooksCustomFieldSlot? PoNumberField =>
        CustomFields.FirstOrDefault(f => f.Enabled && LooksLikePoNumber(f.Name));

    /// <summary>The two switches an invoice push can't work without are on. The PO field is left
    /// out because Preferences can't be trusted to list it.</summary>
    [JsonIgnore]
    public bool ReadyForInvoices => CustomTransactionNumbers && ClassTrackingPerLine;

    /// <summary>The compact form kept on the connection row.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, StoredJson);

    /// <summary>Reads <see cref="ToJson"/> back. Null for nothing stored or a shape this version
    /// can't read, which the page shows as "not checked yet".</summary>
    public static QuickBooksCompanyCheck? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var check = JsonSerializer.Deserialize<QuickBooksCompanyCheck>(json, StoredJson);
            return check?.CustomFields is null ? null : check;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool LooksLikePoNumber(string? name)
    {
        var letters = NonAlphanumeric().Replace(name ?? string.Empty, string.Empty).ToLowerInvariant();
        return PoName().IsMatch(letters);
    }

    /// <summary>Reads the JSON of <c>GET /preferences</c>. Missing sections read as "off", so an
    /// unexpected shape shows up as unticked rows instead of an error.</summary>
    public static QuickBooksCompanyCheck FromPreferences(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var preferences = Child(root, "Preferences") ?? root;
        var sales = Child(preferences, "SalesFormsPrefs");
        var accounting = Child(preferences, "AccountingInfoPrefs");
        var tax = Child(preferences, "TaxPrefs");

        return new QuickBooksCompanyCheck(
            Flag(sales, "CustomTxnNumbers"),
            Flag(accounting, "ClassTrackingPerTxnLine"),
            Flag(accounting, "ClassTrackingPerTxn"),
            Flag(tax, "UsingSalesTax"),
            Flag(tax, "PartnerTaxEnabled"),
            ReadCustomFields(sales));
    }

    /// <summary>SalesFormsPrefs.CustomField is a list of lists: one holds
    /// <c>SalesFormsPrefs.UseSalesCustomN</c> booleans, another
    /// <c>SalesFormsPrefs.SalesCustomNameN</c> strings. N is the definition id.</summary>
    private static IReadOnlyList<QuickBooksCustomFieldSlot> ReadCustomFields(JsonElement? sales)
    {
        var enabled = new Dictionary<string, bool>();
        var names = new Dictionary<string, string>();
        if (sales is { } s && s.TryGetProperty("CustomField", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object
                    || !group.TryGetProperty("CustomField", out var fields)
                    || fields.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var field in fields.EnumerateArray())
                {
                    var key = field.ValueKind == JsonValueKind.Object && field.TryGetProperty("Name", out var n)
                        ? n.GetString() ?? string.Empty
                        : string.Empty;
                    var use = UseFlag().Match(key);
                    if (use.Success)
                    {
                        enabled[use.Groups[1].Value] = Flag(field, "BooleanValue");
                        continue;
                    }

                    var name = NameSlot().Match(key);
                    if (name.Success && field.TryGetProperty("StringValue", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        names[name.Groups[1].Value] = text.GetString() ?? string.Empty;
                    }
                }
            }
        }

        return names
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            // A name with no flag at all is a field in use: QuickBooks only lists switched-on names
            // on some company versions.
            .Select(pair => new QuickBooksCustomFieldSlot(
                pair.Key, pair.Value.Trim(), !enabled.TryGetValue(pair.Key, out var on) || on))
            .ToList();
    }

    private static JsonElement? Child(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static bool Flag(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var value) && value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("^(customer)?(po|purchaseorder)(no|num|number)?$")]
    private static partial Regex PoName();

    [GeneratedRegex(@"^SalesFormsPrefs\.UseSalesCustom(\d+)$")]
    private static partial Regex UseFlag();

    [GeneratedRegex(@"^SalesFormsPrefs\.SalesCustomName(\d+)$")]
    private static partial Regex NameSlot();
}
