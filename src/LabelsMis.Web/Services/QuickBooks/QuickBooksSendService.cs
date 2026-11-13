using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Invoices;
using Microsoft.EntityFrameworkCore;

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
    public async Task<IReadOnlyList<QuickBooksQueueOutcome>> QueueAsync(
        IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var ids = invoiceIds.Distinct().ToList();
        var invoices = await _db.Invoices.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var notConnected = await NotConnectedReasonAsync(cancellationToken);
        if (notConnected is not null)
        {
            return ids.Select(id => new QuickBooksQueueOutcome(
                id, invoices.GetValueOrDefault(id)?.InvoiceNumber ?? string.Empty, false, notConnected)).ToList();
        }

        var waiting = invoices.Values.Any(i => i.Status != InvoiceStatus.Void && i.QbExportedAt is null && !i.NonBillable)
            ? await _invoiceService.GetWaitingReasonsAsync(cancellationToken)
            : [];
        var now = DateTime.UtcNow;
        var outcomes = new List<QuickBooksQueueOutcome>();
        foreach (var id in ids)
        {
            if (!invoices.TryGetValue(id, out var invoice))
            {
                outcomes.Add(new QuickBooksQueueOutcome(id, string.Empty, false, "Invoice not found."));
                continue;
            }

            var reason = WhyNotQueue(invoice, waiting.GetValueOrDefault(id));
            if (reason is null)
            {
                try
                {
                    invoice.QueueForQuickBooks(userId, now);
                }
                catch (InvalidOperationException ex)
                {
                    reason = ex.Message;
                }
            }

            outcomes.Add(new QuickBooksQueueOutcome(id, invoice.InvoiceNumber, reason is null, reason));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return outcomes;
    }

    /// <summary>Sends a "Needs attention" invoice again (back in line, attempts reset).</summary>
    public async Task RetryAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var invoice = await LoadAsync(invoiceId, cancellationToken);
        if (invoice.QbExportStatus != QuickBooksExportStatus.NeedsAttention)
        {
            throw new InvalidOperationException("Only an invoice that needs attention can be retried.");
        }

        if (await NotConnectedReasonAsync(cancellationToken) is { } notConnected)
        {
            throw new InvalidOperationException(notConnected);
        }

        if (invoice.Status != InvoiceStatus.Void && invoice.QbExportedAt is null && !invoice.NonBillable)
        {
            var waiting = await _invoiceService.GetWaitingReasonsAsync(invoiceId, cancellationToken);
            if (waiting.Count > 0)
            {
                throw new InvalidOperationException($"{invoice.InvoiceNumber} isn't ready for QuickBooks: {string.Join("; ", waiting)}.");
            }
        }

        invoice.QueueForQuickBooks(userId, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Takes a queued invoice out of line, or clears "Needs attention" after the user
    /// settled it another way.</summary>
    public async Task CancelAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var invoice = await LoadAsync(invoiceId, cancellationToken);
        if (invoice.QbExportStatus == QuickBooksExportStatus.None)
        {
            return;
        }

        invoice.CancelQuickBooksSend(userId, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Hold the email for this invoice, or send it once to another address.</summary>
    public async Task SetSendOptionsAsync(
        Guid invoiceId, bool holdSend, string? sendToOverride, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var invoice = await LoadAsync(invoiceId, cancellationToken);
        if (invoice.QbExportStatus == QuickBooksExportStatus.Sending)
        {
            throw new InvalidOperationException("This invoice is being sent to QuickBooks right now; try again in a moment.");
        }

        invoice.SetQuickBooksSendOptions(holdSend, sendToOverride, userId, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The QuickBooks state of each invoice asked about (missing ids are left out).</summary>
    public async Task<IReadOnlyList<QuickBooksSendState>> GetStatesAsync(
        IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
    {
        var ids = invoiceIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var environment = await _db.QuickBooksConnections.AsNoTracking()
            .Select(c => (QuickBooksEnvironment?)c.Environment)
            .FirstOrDefaultAsync(cancellationToken);
        var rows = await _db.Invoices.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.CustomerId,
                i.QbExportStatus,
                i.QbInvoiceId,
                i.QbExportedAt,
                i.QbVoidedAt,
                i.QbAttemptCount,
                i.QbLastAttemptAt,
                i.QbNextAttemptAt,
                i.QbLastError,
                i.QbLastErrorCode,
                i.QbTotal,
                i.QbTax,
                i.QbHoldSend,
                i.QbSendToOverride
            })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        return ids.Where(rows.ContainsKey)
            .Select(id => rows[id])
            .Select(i => new QuickBooksSendState(
                i.Id,
                i.QbExportStatus,
                i.QbInvoiceId is not null && i.QbExportedAt is not null,
                i.QbVoidedAt is not null,
                i.QbInvoiceId,
                environment is { } env ? QuickBooksLinks.Invoice(env, i.QbInvoiceId) : null,
                i.QbAttemptCount,
                i.QbLastAttemptAt,
                i.QbNextAttemptAt,
                i.QbExportStatus == QuickBooksExportStatus.None ? null : i.QbLastError,
                i.QbExportStatus == QuickBooksExportStatus.NeedsAttention
                    ? QuickBooksErrorMessages.FixUrlFor(i.QbLastErrorCode, i.Id, i.CustomerId)
                    : null,
                i.QbTotal,
                i.QbTax,
                i.QbHoldSend,
                i.QbSendToOverride))
            .ToList();
    }

    /// <summary>Looks QuickBooks up for an invoice with this invoice's number. Found: records it
    /// as in QuickBooks (id, sync token, QuickBooks' total and tax), so it is never created twice.
    /// Not found: says so and changes nothing.</summary>
    public async Task<QuickBooksLinkResult> LinkExistingAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var invoice = await _db.Invoices.Include(i => i.Customer).SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status == InvoiceStatus.Void)
        {
            throw new InvalidOperationException("A void invoice can't be linked to QuickBooks.");
        }

        if (invoice.NonBillable)
        {
            throw new InvalidOperationException("Non-billable invoices are closed without a QuickBooks record.");
        }

        if (invoice.IsLinkedToQuickBooks)
        {
            throw new InvalidOperationException($"{invoice.InvoiceNumber} is already linked to QuickBooks.");
        }

        if (invoice.QbExportStatus == QuickBooksExportStatus.Sending)
        {
            throw new InvalidOperationException("This invoice is being sent to QuickBooks right now; try again in a moment.");
        }

        if (await NotConnectedReasonAsync(cancellationToken) is { } notConnected)
        {
            throw new InvalidOperationException(notConnected);
        }

        var docNumber = await QuickBooksNumberAsync(invoice.InvoiceNumber, cancellationToken);
        QuickBooksInvoiceAnswer? found;
        try
        {
            found = await QuickBooksInvoiceSender.FindByNumberAsync(
                _gateway, docNumber, _gateway.CallerFor(invoiceId: invoice.Id), cancellationToken);
        }
        catch (QuickBooksApiException ex)
        {
            throw new InvalidOperationException(QuickBooksErrorMessages.Describe(ex, invoice.Id, invoice.CustomerId).Message, ex);
        }
        catch (QuickBooksNotConnectedException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (QuickBooksAuthException ex)
        {
            throw new InvalidOperationException($"QuickBooks didn't accept LabelShop's sign-in ({ex.Message}).", ex);
        }

        if (found is null)
        {
            return new QuickBooksLinkResult(false, $"QuickBooks has no invoice numbered {docNumber}. Nothing was changed.");
        }

        if (!QuickBooksInvoiceSender.IsSameCustomer(found, invoice.Customer.QuickBooksCustomerId))
        {
            return new QuickBooksLinkResult(
                false, QuickBooksInvoiceSender.OtherCustomerMessage(docNumber, found, invoice.Customer.Name));
        }

        invoice.RecordInQuickBooks(found.Id, found.SyncToken, found.TotalAmt, found.TotalTax, null, userId, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new QuickBooksLinkResult(
            true,
            docNumber == invoice.InvoiceNumber
                ? $"Linked {invoice.InvoiceNumber} to the QuickBooks invoice with the same number."
                : $"Linked {invoice.InvoiceNumber} to QuickBooks invoice {docNumber}.");
    }

    /// <summary>The number QuickBooks knows a LabelShop invoice by: the LabelShop number, or only the
    /// number at its end when Setup → QuickBooks says so.</summary>
    public async Task<string> QuickBooksNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        var settings = (await _db.QuickBooksSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.ToPushSettings()
            ?? QuickBooksPushSettings.Default;
        return settings.DocNumberFor(invoiceNumber);
    }

    /// <summary>Why the invoice can't be put in line, or null when it can.</summary>
    private static string? WhyNotQueue(Invoice invoice, IReadOnlyList<string>? waitingReasons)
    {
        if (invoice.QbExportStatus is QuickBooksExportStatus.Queued or QuickBooksExportStatus.Sending)
        {
            return "Already on its way to QuickBooks.";
        }

        if (invoice.Status == InvoiceStatus.Void)
        {
            return invoice.QbInvoiceId is null
                ? "Void, and never reached QuickBooks: nothing to send."
                : invoice.QbVoidedAt is not null
                    ? "Already void in QuickBooks."
                    : null;
        }

        if (invoice.NonBillable)
        {
            return "Non-billable: closed without a QuickBooks record.";
        }

        if (invoice.QbExportedAt is not null)
        {
            return "Already in QuickBooks. To send a change, unmark it exported, make the change, then send it again.";
        }

        return waitingReasons is { Count: > 0 } ? $"Not ready: {string.Join("; ", waitingReasons)}." : null;
    }

    /// <summary>Null when invoices can go to QuickBooks; otherwise what to tell the user.</summary>
    private async Task<string?> NotConnectedReasonAsync(CancellationToken cancellationToken)
    {
        var connection = await _db.QuickBooksConnections.AsNoTracking()
            .Select(c => new { Linked = c.RealmIdProtected != null && c.RefreshTokenProtected != null, c.NeedsReconnect })
            .FirstOrDefaultAsync(cancellationToken);
        if (connection is not { Linked: true })
        {
            return "QuickBooks isn't connected. Connect it on Setup → QuickBooks first.";
        }

        return connection.NeedsReconnect
            ? "QuickBooks needs reconnecting on Setup → QuickBooks before invoices can be sent."
            : null;
    }

    private async Task<Invoice> LoadAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await _db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
        ?? throw new InvalidOperationException("Invoice not found.");

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
