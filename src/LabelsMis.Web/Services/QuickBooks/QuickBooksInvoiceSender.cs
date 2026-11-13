using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Invoices;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>What one pass of the sender did with an invoice.</summary>
public enum QuickBooksSendOutcome
{
    /// <summary>The invoice wasn't being sent any more (cancelled, or claimed by nobody).</summary>
    Skipped,
    Created,

    /// <summary>QuickBooks already had an invoice with this number for this customer; it is now linked.</summary>
    Linked,

    /// <summary>The QuickBooks invoice LabelShop made earlier was changed to match (after "unmark exported").</summary>
    Updated,
    Voided,

    /// <summary>A void invoice that never reached QuickBooks: nothing to do there.</summary>
    NothingToVoid,

    /// <summary>A temporary failure; back in line for a later try.</summary>
    Deferred,
    NeedsAttention
}

public sealed record QuickBooksSendResult(QuickBooksSendOutcome Outcome, string? Message = null);

/// <summary>
/// Takes one claimed invoice (status Sending) to QuickBooks: build and check the payload, look the
/// number up (found: link it, never a second copy), create — or update the QuickBooks invoice it
/// already has, or void it for a void LabelShop invoice — then email it when the send setting says
/// "send now", and record QuickBooks' id, version, total and tax. Temporary failures go back in
/// line with a growing wait; anything a person must fix stops with a message saying what.
/// </summary>
public class QuickBooksInvoiceSender(
    LabelsMisDbContext db,
    InvoiceService invoiceService,
    QuickBooksInvoiceService previewService,
    QuickBooksCatalogService catalogService,
    QuickBooksGateway gateway,
    TimeProvider time,
    ILogger<QuickBooksInvoiceSender> logger)
{
    public const string LookupOperation = "invoice.lookup";
    public const string ReadOperation = "invoice.read";
    public const string CreateOperation = "invoice.create";
    public const string UpdateOperation = "invoice.update";
    public const string VoidOperation = "invoice.void";
    public const string EmailOperation = "invoice.email";

    /// <summary>The waits after the first, second, third and fourth temporary failure; the fifth
    /// stops the invoice for a person.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    /// <summary>Every invoice call carries it, or the PO custom field is silently dropped (Q8 check 5).</summary>
    public static readonly IReadOnlyDictionary<string, string> IncludeCustomFields =
        new Dictionary<string, string> { ["include"] = "enhancedAllCustomFields" };

    public async Task<QuickBooksSendResult> SendAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.Include(i => i.Customer).SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null || invoice.QbExportStatus != QuickBooksExportStatus.Sending)
        {
            return new QuickBooksSendResult(QuickBooksSendOutcome.Skipped);
        }

        var actorId = invoice.QbQueuedById ?? invoice.ModifiedById ?? invoice.CreatedById;
        var caller = new QuickBooksCaller(actorId, Automatic: true, InvoiceId: invoice.Id);
        try
        {
            return invoice.Status == InvoiceStatus.Void
                ? await VoidAsync(invoice, caller, cancellationToken)
                : await CreateOrUpdateAsync(invoice, caller, cancellationToken);
        }
        catch (QuickBooksApiException ex) when (ex.IsTransient || ex.Kind == QuickBooksErrorKind.Unauthorized)
        {
            var text = QuickBooksErrorMessages.Describe(ex, invoice.Id, invoice.CustomerId);
            return await DeferOrStopAsync(invoice, QuickBooksErrorMessages.CodeOf(ex), text.Message);
        }
        catch (QuickBooksApiException ex)
        {
            var text = QuickBooksErrorMessages.Describe(ex, invoice.Id, invoice.CustomerId);
            return await StopAsync(invoice, text.Message, QuickBooksErrorMessages.CodeOf(ex));
        }
        catch (QuickBooksNotConnectedException ex)
        {
            // The worker stops claiming while QuickBooks isn't connected, so this waits for the reconnect.
            invoice.DeferQuickBooksAttempt(ex.Message, "not-connected", Now + RetryDelays[0]);
            await db.SaveChangesAsync(CancellationToken.None);
            return new QuickBooksSendResult(QuickBooksSendOutcome.Deferred, ex.Message);
        }
        catch (QuickBooksAuthException ex)
        {
            return await DeferOrStopAsync(
                invoice, "sign-in", $"QuickBooks didn't accept LabelShop's sign-in ({ex.Message}). It will try again; if this keeps happening, reconnect on Setup → QuickBooks.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending invoice {InvoiceId} to QuickBooks failed unexpectedly", invoice.Id);
            return await StopAsync(
                invoice, $"LabelShop hit an unexpected problem sending this invoice: {ex.Message}", QuickBooksErrorMessages.Unexpected);
        }
    }

    /// <summary>Looks QuickBooks up for an invoice with this number: the number QuickBooks knows the
    /// invoice by (<see cref="QuickBooksPushSettings.DocNumberFor"/>), not always LabelShop's own.</summary>
    public static async Task<QuickBooksInvoiceAnswer?> FindByNumberAsync(
        QuickBooksGateway gateway, string docNumber, QuickBooksCaller caller, CancellationToken cancellationToken)
    {
        var response = await gateway.QueryAsync(
            QuickBooksInvoiceAnswer.NumberQuery(docNumber), LookupOperation, caller, IncludeCustomFields, cancellationToken);
        return QuickBooksInvoiceAnswer.FirstFromQuery(response.Body);
    }

    /// <summary>A QuickBooks invoice found by number is this invoice only when it is for the same
    /// customer (or LabelShop can't tell, because the customer isn't linked).</summary>
    public static bool IsSameCustomer(QuickBooksInvoiceAnswer found, string? quickBooksCustomerId) =>
        string.IsNullOrWhiteSpace(quickBooksCustomerId)
        || string.IsNullOrWhiteSpace(found.CustomerId)
        || string.Equals(found.CustomerId.Trim(), quickBooksCustomerId.Trim(), StringComparison.Ordinal);

    public static string OtherCustomerMessage(string docNumber, QuickBooksInvoiceAnswer found, string customerName) =>
        $"QuickBooks already has an invoice numbered {docNumber}, but for {found.CustomerName ?? "another customer"}, not "
        + $"{customerName}. LabelShop won't link or overwrite it: open it in QuickBooks and give one of the two a new number.";

    /// <summary>The preview, in the shape the payload builder takes.</summary>
    public static QuickBooksInvoiceSource ToSource(QbInvoicePreview preview, PaymentTerms terms, bool holdSend, string? sendToOverride) =>
        new(
            preview.InvoiceId,
            preview.DocNumber,
            preview.TxnDate,
            preview.DueDate,
            terms,
            preview.CustomerId,
            preview.CustomerName,
            preview.QuickBooksCustomerId,
            preview.CustomerPoNumber,
            preview.BillingEmail,
            Address(preview.BillTo),
            Address(preview.ShipTo),
            preview.Lines
                .Select(l => new QuickBooksLineSource(
                    l.LineNumber,
                    l.Item,
                    l.Class,
                    !string.IsNullOrWhiteSpace(l.ItemOverride),
                    !string.IsNullOrWhiteSpace(l.ClassOverride),
                    l.Description,
                    l.Quantity,
                    l.Rate,
                    l.LabelShopAmount))
                .ToList(),
            preview.Shipping.Item,
            preview.Shipping.Class,
            preview.Shipping.Amount,
            holdSend,
            sendToOverride);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private async Task<QuickBooksSendResult> CreateOrUpdateAsync(Invoice invoice, QuickBooksCaller caller, CancellationToken cancellationToken)
    {
        if (invoice.NonBillable)
        {
            return await StopAsync(invoice, "This invoice is non-billable, so it doesn't go to QuickBooks.", QuickBooksErrorMessages.NotReady);
        }

        var waiting = await invoiceService.GetWaitingReasonsAsync(invoice.Id, cancellationToken);
        if (waiting.Count > 0)
        {
            return await StopAsync(
                invoice, $"Not ready for QuickBooks any more: {string.Join("; ", waiting)}.", QuickBooksErrorMessages.NotReady);
        }

        var preview = await previewService.GetPreviewAsync(invoice.Id, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        var catalog = await catalogService.GetLookupAsync(cancellationToken);
        var settings = (await db.QuickBooksSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.ToPushSettings()
            ?? QuickBooksPushSettings.Default;
        var source = ToSource(preview, invoice.Customer.Terms, invoice.QbHoldSend, invoice.QbSendToOverride);
        var built = QuickBooksInvoicePayloadBuilder.Build(source, catalog, settings);
        if (built.Payload is not { } payload)
        {
            return await StopAsync(invoice, string.Join(" ", built.Problems.Select(p => p.Message)), built.Problems[0].Code);
        }

        QuickBooksInvoiceAnswer saved;
        QuickBooksSendOutcome outcome;
        if (invoice.QbInvoiceId is null)
        {
            // Look before creating: an earlier attempt may have created it and been cut off before
            // recording it, or someone keyed it by hand. Either way it is linked, never made twice.
            var found = await FindByNumberAsync(gateway, source.DocNumber, caller, cancellationToken);
            if (found is not null)
            {
                return await LinkFoundAsync(invoice, found, source.DocNumber, source.QuickBooksCustomerId, caller.ActorId);
            }

            try
            {
                var response = await gateway.WriteAsync(
                    "invoice",
                    payload.Json,
                    CreateOperation,
                    QuickBooksInvoicePayload.RequestId(invoice.Id, "create", payload.Json),
                    IncludeCustomFields,
                    caller,
                    cancellationToken);
                saved = Answer(response);
                outcome = QuickBooksSendOutcome.Created;
            }
            catch (QuickBooksApiException ex) when (ex.Code == "6140")
            {
                // The number is taken: whatever has it was not found a moment ago, so look again.
                var duplicate = await FindByNumberAsync(gateway, source.DocNumber, caller, cancellationToken);
                if (duplicate is null)
                {
                    throw;
                }

                return await LinkFoundAsync(invoice, duplicate, source.DocNumber, source.QuickBooksCustomerId, caller.ActorId);
            }
        }
        else
        {
            saved = await UpdateAsync(invoice, invoice.QbInvoiceId, payload, caller, cancellationToken);
            outcome = QuickBooksSendOutcome.Updated;
        }

        // QuickBooks has it now: record that whatever happens to the request.
        invoice.RecordInQuickBooks(saved.Id, saved.SyncToken, saved.TotalAmt, saved.TotalTax, payload.Hash, caller.ActorId, Now);
        await db.SaveChangesAsync(CancellationToken.None);

        if (outcome == QuickBooksSendOutcome.Created && payload.EmailNow)
        {
            await EmailAsync(invoice, saved, payload, caller, cancellationToken);
        }

        return new QuickBooksSendResult(outcome);
    }

    /// <summary>Q21: the invoice was unmarked and changed; the same QuickBooks invoice is updated.
    /// The version stamp is read fresh first; a clash (5010) is read again and tried once more.</summary>
    private async Task<QuickBooksInvoiceAnswer> UpdateAsync(
        Invoice invoice, string quickBooksId, QuickBooksInvoicePayload payload, QuickBooksCaller caller, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var current = await ReadAsync(quickBooksId, caller, cancellationToken);
            var json = payload.UpdateJson(current.Id, current.SyncToken ?? "0");
            try
            {
                var response = await gateway.WriteAsync(
                    "invoice",
                    json,
                    UpdateOperation,
                    QuickBooksInvoicePayload.RequestId(invoice.Id, "update", json),
                    IncludeCustomFields,
                    caller,
                    cancellationToken);
                return Answer(response);
            }
            catch (QuickBooksApiException ex) when (ex.Code == "5010" && attempt == 1)
            {
                // Someone saved it in QuickBooks in between: read the new version and go again.
            }
        }
    }

    /// <summary>Q20: a void LabelShop invoice is voided in QuickBooks — never deleted, and never
    /// when QuickBooks shows a payment on it.</summary>
    private async Task<QuickBooksSendResult> VoidAsync(Invoice invoice, QuickBooksCaller caller, CancellationToken cancellationToken)
    {
        var quickBooksId = invoice.QbInvoiceId;
        if (quickBooksId is null)
        {
            // Voided while its create was still in line: if an attempt got it into QuickBooks
            // without recording it, it is under the number it was sent with.
            var settings = (await db.QuickBooksSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.ToPushSettings()
                ?? QuickBooksPushSettings.Default;
            var found = await FindByNumberAsync(gateway, settings.DocNumberFor(invoice.InvoiceNumber), caller, cancellationToken);
            if (found is null || !IsSameCustomer(found, invoice.Customer.QuickBooksCustomerId))
            {
                invoice.ReleaseStuckQuickBooksAttempt();
                invoice.CancelQuickBooksSend(caller.ActorId, Now);
                await db.SaveChangesAsync(CancellationToken.None);
                return new QuickBooksSendResult(QuickBooksSendOutcome.NothingToVoid);
            }

            quickBooksId = found.Id;
        }

        for (var attempt = 1; ; attempt++)
        {
            var current = await ReadAsync(quickBooksId, caller, cancellationToken);
            if (current.IsVoided)
            {
                invoice.RecordVoidedInQuickBooks(current.SyncToken, caller.ActorId, Now);
                await db.SaveChangesAsync(CancellationToken.None);
                return new QuickBooksSendResult(QuickBooksSendOutcome.Voided);
            }

            if (current.HasPayment)
            {
                return await StopAsync(
                    invoice,
                    $"QuickBooks shows a payment of {current.TotalAmt - current.Balance:C2} on this invoice, so LabelShop won't "
                    + "void it there. Sort the payment out in QuickBooks (or void the invoice there yourself), then retry, or "
                    + "choose \"Settled by hand\".",
                    QuickBooksErrorMessages.Paid);
            }

            var json = QuickBooksInvoicePayload.VoidJson(current.Id, current.SyncToken ?? "0");
            try
            {
                var response = await gateway.WriteAsync(
                    "invoice",
                    json,
                    VoidOperation,
                    QuickBooksInvoicePayload.RequestId(invoice.Id, "void", json),
                    new Dictionary<string, string>(IncludeCustomFields) { ["operation"] = "void" },
                    caller,
                    cancellationToken);
                invoice.RecordVoidedInQuickBooks(QuickBooksInvoiceAnswer.FromEntity(response.Body)?.SyncToken, caller.ActorId, Now);
                await db.SaveChangesAsync(CancellationToken.None);
                return new QuickBooksSendResult(QuickBooksSendOutcome.Voided);
            }
            catch (QuickBooksApiException ex) when (ex.Code == "5010" && attempt == 1)
            {
                // Changed in QuickBooks in between (a payment, perhaps): read it again and re-check.
            }
        }
    }

    /// <summary>"Send now": QuickBooks emails the invoice it just made, to the one-off address when
    /// the invoice has one. A refusal leaves the invoice in QuickBooks and asks a person to send it.</summary>
    private async Task EmailAsync(
        Invoice invoice, QuickBooksInvoiceAnswer saved, QuickBooksInvoicePayload payload, QuickBooksCaller caller, CancellationToken cancellationToken)
    {
        var parameters = payload.SendTo is null ? null : new Dictionary<string, string> { ["sendTo"] = payload.SendTo };
        try
        {
            var response = await gateway.WriteAsync(
                $"invoice/{Uri.EscapeDataString(saved.Id)}/send",
                null,
                EmailOperation,
                QuickBooksInvoicePayload.RequestId(invoice.Id, "send", $"{saved.Id}\n{payload.SendTo ?? payload.BillEmail}\n{payload.Hash}"),
                parameters,
                caller,
                cancellationToken);
            if (QuickBooksInvoiceAnswer.FromEntity(response.Body) is { SyncToken: not null } sent)
            {
                invoice.RecordInQuickBooks(sent.Id, sent.SyncToken, sent.TotalAmt ?? saved.TotalAmt, sent.TotalTax ?? saved.TotalTax,
                    payload.Hash, caller.ActorId, Now);
            }
        }
        catch (QuickBooksApiException ex)
        {
            var text = QuickBooksErrorMessages.Describe(ex, invoice.Id, invoice.CustomerId);
            invoice.FlagQuickBooksAttention(
                $"It is in QuickBooks, but QuickBooks didn't email it: {text.Message} Send it from QuickBooks, then choose \"Settled by hand\".",
                QuickBooksErrorMessages.NotEmailed,
                Now);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<QuickBooksSendResult> LinkFoundAsync(
        Invoice invoice, QuickBooksInvoiceAnswer found, string docNumber, string? quickBooksCustomerId, Guid actorId)
    {
        if (!IsSameCustomer(found, quickBooksCustomerId))
        {
            return await StopAsync(
                invoice, OtherCustomerMessage(docNumber, found, invoice.Customer.Name), QuickBooksErrorMessages.OtherCustomer);
        }

        invoice.RecordInQuickBooks(found.Id, found.SyncToken, found.TotalAmt, found.TotalTax, null, actorId, Now);
        await db.SaveChangesAsync(CancellationToken.None);
        return new QuickBooksSendResult(QuickBooksSendOutcome.Linked);
    }

    private async Task<QuickBooksInvoiceAnswer> ReadAsync(string quickBooksId, QuickBooksCaller caller, CancellationToken cancellationToken)
    {
        var response = await gateway.ReadAsync(
            $"invoice/{Uri.EscapeDataString(quickBooksId)}", ReadOperation, caller, IncludeCustomFields, cancellationToken);
        return Answer(response);
    }

    private static QuickBooksInvoiceAnswer Answer(QuickBooksApiResponse response) =>
        QuickBooksInvoiceAnswer.FromEntity(response.Body)
        ?? throw new InvalidOperationException("QuickBooks answered without the invoice.");

    /// <summary>A temporary failure: back in line after the next wait in <see cref="RetryDelays"/>,
    /// or stopped for a person once they are used up.</summary>
    private async Task<QuickBooksSendResult> DeferOrStopAsync(Invoice invoice, string code, string message)
    {
        if (invoice.QbAttemptCount > RetryDelays.Count)
        {
            return await StopAsync(invoice, $"Tried {invoice.QbAttemptCount} times. {message}", code);
        }

        var wait = RetryDelays[Math.Max(0, invoice.QbAttemptCount - 1)];
        invoice.DeferQuickBooksAttempt(message, code, Now + wait);
        await db.SaveChangesAsync(CancellationToken.None);
        return new QuickBooksSendResult(QuickBooksSendOutcome.Deferred, message);
    }

    private async Task<QuickBooksSendResult> StopAsync(Invoice invoice, string message, string code)
    {
        invoice.FlagQuickBooksAttention(message, code, Now);
        await db.SaveChangesAsync(CancellationToken.None);
        return new QuickBooksSendResult(QuickBooksSendOutcome.NeedsAttention, message);
    }

    private static QuickBooksAddressSource? Address(QbAddress? address) =>
        address is null
            ? null
            : new QuickBooksAddressSource(address.Name, address.Line1, address.Line2, address.City, address.State, address.Zip, address.Country);
}
