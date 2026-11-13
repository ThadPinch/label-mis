using System.Text.Json;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Invoicing;

/// <summary>The part of Setup → QuickBooks that only matters once invoices are sent through the
/// API: which QuickBooks payment term each LabelShop term becomes, which QuickBooks custom field
/// carries the customer's PO number, what happens to the invoice's email, and which invoice number
/// QuickBooks gets.</summary>
public sealed record QuickBooksPushSettings(
    IReadOnlyDictionary<PaymentTerms, string> Terms,
    string? PoFieldId,
    string? PoFieldName,
    QuickBooksSendMode SendMode,
    QuickBooksInvoiceNumberFormat NumberFormat = QuickBooksInvoiceNumberFormat.Full)
{
    public static QuickBooksPushSettings Default { get; } =
        new(new Dictionary<PaymentTerms, string>(), null, null, QuickBooksSendMode.SendLater);

    /// <summary>The QuickBooks term name for a LabelShop term, or null when none is chosen (the
    /// invoice then carries only its due date).</summary>
    public string? TermFor(PaymentTerms terms) =>
        Terms.TryGetValue(terms, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;

    /// <summary>The number QuickBooks knows this LabelShop invoice by: what it is sent under and
    /// what it is looked up under.</summary>
    public string DocNumberFor(string invoiceNumber) => QuickBooksDocNumber.For(invoiceNumber, NumberFormat);

    /// <summary>The terms map as it is stored: <c>{"Net30":"Net 30"}</c>, blanks left out.</summary>
    public static string SerializeTerms(IReadOnlyDictionary<PaymentTerms, string> terms) =>
        JsonSerializer.Serialize(terms
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => (int)pair.Key)
            .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.Trim()));

    /// <summary>Reads a stored map. Anything unreadable, and any term name LabelShop no longer has,
    /// is dropped rather than failing the page.</summary>
    public static IReadOnlyDictionary<PaymentTerms, string> ParseTerms(string? json)
    {
        var result = new Dictionary<PaymentTerms, string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            foreach (var (key, value) in stored ?? [])
            {
                if (Enum.TryParse<PaymentTerms>(key, out var terms) && Enum.IsDefined(terms) && !string.IsNullOrWhiteSpace(value))
                {
                    result[terms] = value.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // An empty map is the safe reading: invoices then carry a due date and no terms.
        }

        return result;
    }
}
