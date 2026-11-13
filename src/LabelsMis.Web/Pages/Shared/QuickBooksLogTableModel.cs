using LabelsMis.Web.Services.QuickBooks;

namespace LabelsMis.Web.Pages.Shared;

/// <summary>The QuickBooks activity-log table (<c>_QuickBooksLogTable</c>): the Activity log page
/// shows every row with its invoice; an invoice's own history leaves the invoice column out.</summary>
public sealed record QuickBooksLogTableModel(
    IReadOnlyList<QuickBooksLogRow> Rows,
    Func<DateTime, string> Local,
    bool ShowInvoice = true,
    string? TableId = null);
