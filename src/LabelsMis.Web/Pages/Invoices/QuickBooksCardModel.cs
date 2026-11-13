using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.QuickBooks;

namespace LabelsMis.Web.Pages.Invoices;

/// <summary>What the QuickBooks card (<c>_QuickBooksCard</c>) on the invoice and preview pages
/// needs. The page that hosts the card implements its handlers: <c>QbSend</c>, <c>QbRetry</c>,
/// <c>QbCancel</c>, and on the invoice page also <c>QbLink</c> and <c>QbSendOptions</c>.</summary>
public sealed record QuickBooksCardModel(
    Guid InvoiceId,
    string InvoiceNumber,
    QuickBooksSendState? State,
    QuickBooksConnectionState Connection,
    bool CanEdit,
    bool IsVoid,
    bool NonBillable,
    bool ExportedByHand,
    IReadOnlyList<string> WaitingReasons,
    decimal LabelShopTotal,
    decimal LabelShopTax,
    Func<DateTime, string> Local,
    bool Compact = false)
{
    public bool Connected => Connection == QuickBooksConnectionState.Connected;
    public QuickBooksExportStatus Status => State?.Status ?? QuickBooksExportStatus.None;
    public bool InQuickBooks => State?.InQuickBooks == true;
    public bool VoidedInQuickBooks => State?.VoidedInQuickBooks == true;
    public bool Busy => State?.IsBusy == true;
    public bool NeedsAttention => Status == QuickBooksExportStatus.NeedsAttention;

    /// <summary>Send: an invoice QuickBooks doesn't have yet and nothing is under way for.</summary>
    public bool CanSend => CanEdit && Connected && Status == QuickBooksExportStatus.None
        && !InQuickBooks && !IsVoid && !NonBillable && !ExportedByHand;

    /// <summary>The Send button shows but is disabled while the invoice is waiting for something.</summary>
    public bool SendBlocked => WaitingReasons.Count > 0;

    /// <summary>A void invoice whose QuickBooks copy is still live.</summary>
    public bool CanVoidInQuickBooks => CanEdit && Connected && Status == QuickBooksExportStatus.None
        && IsVoid && InQuickBooks && !VoidedInQuickBooks;

    public bool CanRetry => CanEdit && Connected && NeedsAttention;

    /// <summary>Queued: take it out of line. Needs attention: settled by hand.</summary>
    public bool CanCancel => CanEdit && (Status == QuickBooksExportStatus.Queued || NeedsAttention);

    /// <summary>"Already in QuickBooks? Link it": only while LabelShop has no QuickBooks id for it.</summary>
    public bool CanLinkExisting => CanEdit && Connected && !Compact && !Busy
        && State?.QuickBooksInvoiceId is null && !IsVoid && !NonBillable;

    /// <summary>Hold / email-to-instead matter only until the invoice is in QuickBooks.</summary>
    public bool CanSetSendOptions => CanEdit && !Compact && !InQuickBooks && !IsVoid && !NonBillable
        && Status != QuickBooksExportStatus.Sending;

    public decimal? TotalDifference => QuickBooksUi.Difference(LabelShopTotal, State?.QuickBooksTotal);
    public decimal? TaxDifference => QuickBooksUi.Difference(LabelShopTax, State?.QuickBooksTax);
}
