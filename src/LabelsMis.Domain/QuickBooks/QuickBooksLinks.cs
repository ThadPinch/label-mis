using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>Addresses of records in QuickBooks Online's own screens ("Open in QuickBooks").</summary>
public static class QuickBooksLinks
{
    public static string AppBase(QuickBooksEnvironment environment) =>
        environment == QuickBooksEnvironment.Production
            ? "https://app.qbo.intuit.com/app"
            : "https://app.sandbox.qbo.intuit.com/app";

    public static string? Invoice(QuickBooksEnvironment environment, string? quickBooksInvoiceId) =>
        string.IsNullOrWhiteSpace(quickBooksInvoiceId)
            ? null
            : $"{AppBase(environment)}/invoice?txnId={Uri.EscapeDataString(quickBooksInvoiceId.Trim())}";

    public static string? Customer(QuickBooksEnvironment environment, string? quickBooksCustomerId) =>
        string.IsNullOrWhiteSpace(quickBooksCustomerId)
            ? null
            : $"{AppBase(environment)}/customerdetail?nameId={Uri.EscapeDataString(quickBooksCustomerId.Trim())}";
}
