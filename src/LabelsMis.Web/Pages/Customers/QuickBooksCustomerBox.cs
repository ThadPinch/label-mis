using LabelsMis.Domain.QuickBooks;
using LabelsMis.Web.Services.QuickBooks;

namespace LabelsMis.Web.Pages.Customers;

/// <summary>What the QuickBooks box on the customer form shows: the link, whether QuickBooks can be
/// asked anything right now, a search and its results, and the outcome of the last action.</summary>
public sealed record QuickBooksCustomerBox(
    Guid CustomerId,
    string? LinkedId,
    string? LinkedName,
    QuickBooksCustomerAvailability Availability,
    bool CanEdit,
    string? SearchText = null,
    IReadOnlyList<QuickBooksCustomerSummary>? SearchResults = null,
    string? Message = null,
    string? Error = null,
    string? DuplicateId = null,
    string? DuplicateName = null)
{
    public bool IsLinked => !string.IsNullOrWhiteSpace(LinkedId);

    public string? OpenUrl => Availability.OpenUrl(LinkedId);
}

/// <summary>Turns what can go wrong talking to QuickBooks into a sentence for the page. Null for
/// anything unexpected, which is left to the error page.</summary>
public static class QuickBooksCustomerPageErrors
{
    public static string? Describe(Exception exception) => exception switch
    {
        QuickBooksNotConnectedException ex => ex.Message,
        QuickBooksApiException ex => $"QuickBooks said: {ex.Message}",
        QuickBooksAuthException ex => ex.Message,
        InvalidOperationException or ArgumentException or KeyNotFoundException => exception.Message,
        _ => null
    };

    /// <summary>What to say after a link was made.</summary>
    public static string Linked(QuickBooksCustomerLinkResult result) =>
        result.AlsoLinkedTo.Count == 0
            ? $"Linked to \u201c{result.Customer.DisplayName}\u201d in QuickBooks."
            : $"Linked to \u201c{result.Customer.DisplayName}\u201d in QuickBooks. It is also linked to {string.Join(", ", result.AlsoLinkedTo)}, so their invoices land on the same QuickBooks customer.";
}
