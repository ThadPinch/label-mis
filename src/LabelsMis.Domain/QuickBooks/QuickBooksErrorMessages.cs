namespace LabelsMis.Domain.QuickBooks;

/// <summary>LabelShop pages a QuickBooks problem points at, so a message can carry a "fix it here" link.</summary>
public static class QuickBooksFixLinks
{
    public const string Settings = "/settings/quickbooks";

    public static string Customer(Guid customerId) => $"/customers/edit/{customerId}";

    public static string Invoice(Guid invoiceId) => $"/invoices/{invoiceId}";

    /// <summary>The invoice's QuickBooks preview, where a line's product/service and class can be overridden.</summary>
    public static string InvoicePreview(Guid invoiceId) => $"/invoices/{invoiceId}/quickbooks";
}

/// <summary>A problem as the person at the screen reads it, and where to fix it (null when the fix
/// is in QuickBooks itself or is simply to wait).</summary>
public sealed record QuickBooksProblemText(string Message, string? FixUrl);

/// <summary>
/// QuickBooks' fault codes, and LabelShop's own reasons for not sending (codes starting
/// <c>LS-</c>), turned into sentences for accounting staff. The code is what is stored on the
/// invoice (<c>QbLastErrorCode</c>); <see cref="FixUrlFor"/> rebuilds the link from it, so the link
/// never has to be stored. Unknown QuickBooks codes keep Intuit's own words.
/// </summary>
public static class QuickBooksErrorMessages
{
    // LabelShop's own reasons (from the payload check and the send itself).
    public const string Lists = "LS-LISTS";
    public const string Item = "LS-ITEM";
    public const string LineItem = "LS-LINE-ITEM";
    public const string Class = "LS-CLASS";
    public const string LineClass = "LS-LINE-CLASS";
    public const string Customer = "LS-CUSTOMER";
    public const string OtherCustomer = "LS-OTHER-CUSTOMER";
    public const string Email = "LS-EMAIL";
    public const string Number = "LS-NUMBER";
    public const string NoLines = "LS-LINES";
    public const string Total = "LS-TOTAL";
    public const string NotReady = "LS-NOT-READY";
    public const string Paid = "LS-PAID";
    public const string NotEmailed = "LS-NOT-EMAILED";
    public const string Unexpected = "LS-UNEXPECTED";
    public const string Retries = "LS-RETRIES";

    /// <summary>The words for a failed QuickBooks call about one invoice.</summary>
    public static QuickBooksProblemText Describe(QuickBooksApiException exception, Guid invoiceId, Guid customerId) =>
        Describe(CodeOf(exception), exception.Message, invoiceId, customerId, exception.StatusCode);

    /// <summary>The code an exception is filed under: Intuit's fault code, else the HTTP status,
    /// else "network".</summary>
    public static string CodeOf(QuickBooksApiException exception) =>
        exception.Code
        ?? exception.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ?? (exception.Kind == QuickBooksErrorKind.Network ? "network" : "unknown");

    /// <summary>The words for a QuickBooks fault code. <paramref name="intuitDetail"/> is Intuit's
    /// own message; it is quoted where it names the field at fault, and used whole for codes this
    /// class doesn't know.</summary>
    public static QuickBooksProblemText Describe(
        string? code, string? intuitDetail, Guid invoiceId, Guid customerId, int? statusCode = null)
    {
        var detail = string.IsNullOrWhiteSpace(intuitDetail) ? null : intuitDetail.Trim();
        var fix = FixUrlFor(code, invoiceId, customerId);
        var message = code switch
        {
            "610" =>
                "Something this invoice uses (the customer, a product or service, a class or the terms) has been deleted "
                + "or made inactive in QuickBooks. Refresh the lists on Setup → QuickBooks and check the customer's "
                + "QuickBooks link, then retry.",
            "2500" =>
                "QuickBooks doesn't recognise the customer, a product or service, a class or the terms on this invoice "
                + "(one may have been made inactive). Refresh the lists on Setup → QuickBooks and check the customer's "
                + "QuickBooks link, then retry.",
            "2020" =>
                $"QuickBooks says something it needs is missing from this invoice{Quote(detail)}. Check the invoice's "
                + "QuickBooks preview, then retry.",
            "2090" =>
                $"A QuickBooks link on this invoice isn't a QuickBooks id{Quote(detail)}. It is usually the customer's "
                + "QuickBooks link typed in by hand: open the customer, pick the QuickBooks customer with Search "
                + "QuickBooks, then retry.",
            "2050" =>
                $"Some text on this invoice is longer than QuickBooks allows{Quote(detail)}. It is usually the PO "
                + "number or a line description: shorten it, then retry.",
            "5010" =>
                "Someone changed this invoice in QuickBooks while LabelShop was updating it, twice in a row. "
                + "Retry in a minute.",
            "6000" =>
                $"QuickBooks refused the invoice{Quote(detail)}. Fix what it names, then retry.",
            "6070" =>
                "QuickBooks says a line's amount isn't its quantity times its rate. Check the lines on the invoice's "
                + "QuickBooks preview, then retry.",
            "6140" =>
                "QuickBooks already has an invoice with this number and LabelShop couldn't link to it. Open it in "
                + "QuickBooks: if it is this invoice, use \"Already in QuickBooks? Link it\"; if it is a different "
                + "invoice, one of the two needs a new number.",
            "6190" =>
                "QuickBooks won't take new invoices for this company: its subscription has ended or is suspended. "
                + "Sort out the subscription in QuickBooks, then retry.",
            "6210" =>
                "The books are closed in QuickBooks for this invoice's date. Change the closing date in QuickBooks "
                + "(or ask whoever closed the books), then retry.",
            "6240" =>
                "QuickBooks already has a customer with this name. Link the LabelShop customer to that one on the "
                + "customer's page.",
            "3200" or "401" =>
                "QuickBooks refused LabelShop's sign-in. Reconnect on Setup → QuickBooks; invoices go again once it is "
                + "connected.",
            "3100" or "403" =>
                "QuickBooks says LabelShop isn't allowed to do this for the company (the connection may have been "
                + "removed in QuickBooks). Reconnect on Setup → QuickBooks, then retry.",
            "429" =>
                "QuickBooks asked LabelShop to slow down. It will try again in a few minutes.",
            "network" =>
                "LabelShop couldn't reach QuickBooks. It will try again in a few minutes.",
            Lists or Item or LineItem or Class or LineClass or Customer or OtherCustomer or Email or Number or NoLines
                or Total or NotReady or Paid or NotEmailed or Unexpected or Retries =>
                detail ?? "LabelShop could not send this invoice to QuickBooks.",
            _ when IsServerCode(code, statusCode) =>
                "QuickBooks had a problem on its side and LabelShop's retries didn't get through. Retry later.",
            _ => detail is null
                ? $"QuickBooks refused the invoice (error {code ?? "unknown"})."
                : $"QuickBooks refused the invoice: {detail}"
        };

        return new QuickBooksProblemText(message, fix);
    }

    /// <summary>Where to fix a problem filed under <paramref name="code"/>; null when there is
    /// nothing in LabelShop to change.</summary>
    public static string? FixUrlFor(string? code, Guid invoiceId, Guid customerId) => code switch
    {
        "610" or "2500" or "3200" or "3100" or "401" or "403" or Lists or Item or Class => QuickBooksFixLinks.Settings,
        "6240" or "2090" or Customer or Email => QuickBooksFixLinks.Customer(customerId),
        "2020" or "2050" or "6000" or "6070" or LineItem or LineClass => QuickBooksFixLinks.InvoicePreview(invoiceId),
        "6140" or OtherCustomer or Number or NoLines or Total or NotReady => QuickBooksFixLinks.Invoice(invoiceId),
        _ => null
    };

    /// <summary>500-class trouble: an HTTP 5xx, or one of Intuit's 500x / 10000 internal codes.
    /// 5010 (stale object) is not one of them.</summary>
    public static bool IsServerCode(string? code, int? statusCode) =>
        statusCode >= 500
        || code is "500" or "502" or "503" or "504" or "10000"
        || (code is { Length: 4 } && code.StartsWith("500", StringComparison.Ordinal));

    private static string Quote(string? detail) => detail is null ? string.Empty : $" ({detail})";
}
