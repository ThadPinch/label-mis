using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>
/// The invoice number QuickBooks knows a LabelShop invoice by (its <c>DocNumber</c>): the number
/// the invoice is sent under, looked up under and linked by.
/// </summary>
public static class QuickBooksDocNumber
{
    /// <summary>The number QuickBooks gets for <paramref name="invoiceNumber"/>.
    /// <see cref="QuickBooksInvoiceNumberFormat.NumberOnly"/> turns <c>INV-2026-00549</c> into
    /// <c>00549</c>, leading zeros kept. A number that isn't LabelShop's own prefix-year-number
    /// shape (an invoice imported with its old number) is sent as it is.</summary>
    public static string For(string invoiceNumber, QuickBooksInvoiceNumberFormat format)
    {
        var number = invoiceNumber.Trim();
        return format == QuickBooksInvoiceNumberFormat.NumberOnly && NumberAtTheEnd(number) is { } tail ? tail : number;
    }

    /// <summary>The digits after the year of a <c>PREFIX-YYYY-NNNNN</c> number, or null when the
    /// number has another shape.</summary>
    public static string? NumberAtTheEnd(string number)
    {
        var parts = number.Split('-');
        return parts.Length == 3
               && parts[0].Length > 0 && parts[0].All(char.IsAsciiLetter)
               && parts[1].Length == 4 && parts[1].All(char.IsAsciiDigit)
               && parts[2].Length > 0 && parts[2].All(char.IsAsciiDigit)
            ? parts[2]
            : null;
    }
}
