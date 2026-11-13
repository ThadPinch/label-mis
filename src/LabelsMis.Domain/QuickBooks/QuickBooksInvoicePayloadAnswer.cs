using System.Text.Json;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>
/// What LabelShop keeps from QuickBooks' answer about one invoice (a create, update, void, send
/// or read returns <c>{"Invoice":{...}}</c>; a lookup returns <c>{"QueryResponse":{"Invoice":[...]}}</c>).
/// </summary>
public sealed record QuickBooksInvoiceAnswer(
    string Id,
    string? SyncToken,
    string? DocNumber,
    string? CustomerId,
    string? CustomerName,
    decimal? TotalAmt,
    decimal? TotalTax,
    decimal? Balance,
    string? PrivateNote)
{
    /// <summary>QuickBooks has voided it: a void zeroes the total and the balance and sets the
    /// private note to "Voided".</summary>
    public bool IsVoided =>
        TotalAmt == 0m && Balance is null or 0m
        && PrivateNote?.Contains("Voided", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Something has been paid (or credited) against it in QuickBooks.</summary>
    public bool HasPayment => TotalAmt is { } total && Balance is { } balance && balance < total;

    /// <summary>The lookup for an invoice by its number. A quote in the number is escaped the way
    /// QuickBooks' query language wants (<c>\'</c>).</summary>
    public static string NumberQuery(string docNumber) =>
        $"select * from Invoice where DocNumber = '{Escape(docNumber.Trim())}'";

    public static string Escape(string literal) => literal.Replace("'", "\\'", StringComparison.Ordinal);

    /// <summary>The invoice in a create/update/void/send/read answer; null when there is none.</summary>
    public static QuickBooksInvoiceAnswer? FromEntity(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("Invoice", out var invoice)
                ? From(invoice)
                : null;
    }

    /// <summary>The first invoice of a query answer; null when QuickBooks found none.</summary>
    public static QuickBooksInvoiceAnswer? FirstFromQuery(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("QueryResponse", out var response)
            || !response.TryGetProperty("Invoice", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (From(row) is { } answer)
            {
                return answer;
            }
        }

        return null;
    }

    private static QuickBooksInvoiceAnswer? From(JsonElement invoice)
    {
        if (invoice.ValueKind != JsonValueKind.Object || Text(invoice, "Id") is not { Length: > 0 } id)
        {
            return null;
        }

        string? customerId = null;
        string? customerName = null;
        if (invoice.TryGetProperty("CustomerRef", out var customer) && customer.ValueKind == JsonValueKind.Object)
        {
            customerId = Text(customer, "value");
            customerName = Text(customer, "name");
        }

        decimal? totalTax = invoice.TryGetProperty("TxnTaxDetail", out var tax) && tax.ValueKind == JsonValueKind.Object
            ? Number(tax, "TotalTax")
            : null;

        return new QuickBooksInvoiceAnswer(
            id,
            Text(invoice, "SyncToken"),
            Text(invoice, "DocNumber"),
            customerId,
            customerName,
            Number(invoice, "TotalAmt"),
            totalTax,
            Number(invoice, "Balance"),
            Text(invoice, "PrivateNote"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;
}
