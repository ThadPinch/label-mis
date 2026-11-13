using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Invoices;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>What happened to one invoice of a "Send to QuickBooks" request.</summary>
public sealed record QuickBooksQueueOutcome(Guid InvoiceId, string InvoiceNumber, bool Queued, string? Reason);

/// <summary>What the pages show about one invoice's trip to QuickBooks.</summary>
public sealed record QuickBooksSendState(
    Guid InvoiceId,
    QuickBooksExportStatus Status,
    bool InQuickBooks,
    bool VoidedInQuickBooks,
    string? QuickBooksInvoiceId,
    string? OpenInQuickBooksUrl,
    int AttemptCount,
    DateTime? LastAttemptAt,
    DateTime? NextAttemptAt,
    string? LastError,
    string? FixUrl,
    decimal? QuickBooksTotal,
    decimal? QuickBooksTax,
    bool HoldSend,
    string? SendToOverride)
{
    /// <summary>Short words for a status badge: Queued, Sending, Needs attention, In QuickBooks,
    /// Void in QuickBooks, or null when the invoice has nothing to do with QuickBooks yet.</summary>
    public string? Label => Status switch
    {
        QuickBooksExportStatus.Queued => "Queued",
        QuickBooksExportStatus.Sending => "Sending",
        QuickBooksExportStatus.NeedsAttention => "Needs attention",
        _ when VoidedInQuickBooks => "Void in QuickBooks",
        _ when InQuickBooks => "In QuickBooks",
        _ => null
    };

    /// <summary>True while the worker still has something to do; pages poll while any row is busy.</summary>
    public bool IsBusy => Status is QuickBooksExportStatus.Queued or QuickBooksExportStatus.Sending;
}

/// <summary>The answer to "Already in QuickBooks?": linked to the QuickBooks invoice with the same
/// number, or why not.</summary>
public sealed record QuickBooksLinkResult(bool Linked, string Message);

/// <summary>
/// The pages' door to sending invoices through the QuickBooks API. Queueing, retrying, cancelling
/// and the per-invoice send options only change the invoice row; the background
/// <c>QuickBooksSendWorker</c> does the sending. <see cref="LinkExistingAsync"/> is the one call
/// that talks to QuickBooks directly (through <see cref="QuickBooksGateway"/>).
/// Every method throws <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>
/// with a message fit to show the user when the request can't be done.
/// </summary>
public class QuickBooksSendService(
    LabelsMisDbContext db,
    InvoiceService invoiceService,
    QuickBooksGateway gateway,
    ICurrentUserService currentUser)
{
    private readonly LabelsMisDbContext _db = db;
    private readonly InvoiceService _invoiceService = invoiceService;
    private readonly QuickBooksGateway _gateway = gateway;
    private readonly ICurrentUserService _currentUser = currentUser;

    /// <summary>Puts each invoice in line for the worker. Only Ready invoices (no waiting reasons),
    /// void invoices that are in QuickBooks and not yet voided there, are queued; every other one
    /// comes back with <c>Queued = false</c> and the reason. Refused while QuickBooks is not
    /// connected (the reason says so).</summary>
    public Task<IReadOnlyList<QuickBooksQueueOutcome>> QueueAsync(
        IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>Sends a "Needs attention" invoice again (back in line, attempts reset).</summary>
    public Task RetryAsync(Guid invoiceId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>Takes a queued invoice out of line, or clears "Needs attention" after the user
    /// settled it another way.</summary>
    public Task CancelAsync(Guid invoiceId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>Hold the email for this invoice, or send it once to another address.</summary>
    public Task SetSendOptionsAsync(
        Guid invoiceId, bool holdSend, string? sendToOverride, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>The QuickBooks state of each invoice asked about (missing ids are left out).</summary>
    public Task<IReadOnlyList<QuickBooksSendState>> GetStatesAsync(
        IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>Looks QuickBooks up for an invoice with this invoice's number. Found: records it
    /// as in QuickBooks (id, sync token, QuickBooks' total and tax), so it is never created twice.
    /// Not found: says so and changes nothing.</summary>
    public Task<QuickBooksLinkResult> LinkExistingAsync(Guid invoiceId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
