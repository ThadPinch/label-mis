namespace LabelsMis.Domain.QuickBooks;

/// <summary>The names the QuickBooks log files each attempt under. Stored as text, so a name is
/// never changed once rows exist; add new ones instead.</summary>
public static class QuickBooksOperations
{
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string ListsRefresh = "lists.refresh";
    public const string PoFieldFind = "pofield.find";
    public const string TokenRefresh = "token.refresh";
    public const string CompanyRead = "company.read";
    public const string PreferencesRead = "preferences.read";

    // Invoice sends (the sender's own constants carry the same text).
    public const string InvoiceLookup = "invoice.lookup";
    public const string InvoiceRead = "invoice.read";
    public const string InvoiceCreate = "invoice.create";
    public const string InvoiceUpdate = "invoice.update";
    public const string InvoiceVoid = "invoice.void";
    public const string InvoiceEmail = "invoice.email";

    /// <summary>What the log page calls an operation.</summary>
    public static string Describe(string operation) => operation switch
    {
        Connect => "Connect to QuickBooks",
        Disconnect => "Disconnect",
        ListsRefresh => "Refresh the lists",
        PoFieldFind => "Find the PO field",
        TokenRefresh => "Refresh the sign-in",
        CompanyRead => "Read the company name",
        PreferencesRead => "Read the company settings",
        InvoiceLookup => "Look for the invoice number",
        InvoiceRead => "Read the invoice",
        InvoiceCreate => "Create the invoice",
        InvoiceUpdate => "Update the invoice",
        InvoiceVoid => "Void the invoice",
        InvoiceEmail => "Email the invoice from QuickBooks",
        _ => QuickBooksCustomerOperations.Describe(operation) ?? operation
    };
}
