using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LabelsMis.Web.Pages.Invoices;

/// <summary>One invoice's QuickBooks state as the invoice pages' status poll returns it.</summary>
public sealed record QuickBooksStateJson(
    Guid Id,
    string? Label,
    string BadgeClass,
    bool Busy,
    bool InQuickBooks,
    bool NeedsAttention,
    string? LastError,
    string? FixUrl);

/// <summary>Shared bits of the QuickBooks parts of the invoice pages: badges, flash messages
/// and the connection check. No business rules: those live in <see cref="QuickBooksSendService"/>.</summary>
public static class QuickBooksUi
{
    /// <summary>TempData key for a QuickBooks action that worked.</summary>
    public const string MessageKey = "QuickBooksMessage";

    /// <summary>TempData key for the invoices a send request left out, one line each.</summary>
    public const string ProblemsKey = "QuickBooksProblems";

    /// <summary>TempData key for a QuickBooks action that was refused.</summary>
    public const string ErrorKey = "QuickBooksError";

    /// <summary>How often the pages ask for fresh states while something is on its way.</summary>
    public const int PollIntervalMs = 4000;

    /// <summary>The most invoices one status poll asks about.</summary>
    public const int MaxPollIds = 100;

    /// <summary>Bootstrap colour of a status badge; <c>d-none</c> hides it while the invoice has
    /// nothing to do with QuickBooks.</summary>
    public static string BadgeClass(QuickBooksSendState? state) => state switch
    {
        null => "d-none",
        { Status: QuickBooksExportStatus.Queued } => "text-bg-info",
        { Status: QuickBooksExportStatus.Sending } => "text-bg-primary",
        { Status: QuickBooksExportStatus.NeedsAttention } => "text-bg-danger",
        { VoidedInQuickBooks: true } => "text-bg-dark",
        { InQuickBooks: true } => "text-bg-success",
        _ => "d-none"
    };

    /// <summary>A status badge the poll script can find and update in place.</summary>
    public static IHtmlContent Badge(Guid invoiceId, QuickBooksSendState? state, string? elementId = null)
    {
        var tag = new TagBuilder("span");
        tag.AddCssClass("badge qb-badge " + BadgeClass(state));
        tag.Attributes["data-qb-badge"] = string.Empty;
        tag.Attributes["data-invoice-id"] = invoiceId.ToString();
        tag.Attributes["data-busy"] = state?.IsBusy == true ? "true" : "false";
        if (elementId is not null)
        {
            tag.Attributes["id"] = elementId;
        }

        if (state?.Status == QuickBooksExportStatus.NeedsAttention && state.LastError is { } error)
        {
            tag.Attributes["title"] = error;
        }

        tag.InnerHtml.Append(state?.Label ?? string.Empty);
        return tag;
    }

    public static QuickBooksStateJson ToJson(QuickBooksSendState state) => new(
        state.InvoiceId,
        state.Label,
        BadgeClass(state),
        state.IsBusy,
        state.InQuickBooks,
        state.Status == QuickBooksExportStatus.NeedsAttention,
        state.LastError,
        state.FixUrl);

    /// <summary>The flash message after "Send to QuickBooks": how many went in line, and each
    /// invoice that didn't with its reason.</summary>
    public static (string? Queued, IReadOnlyList<string> Problems) Summarise(IReadOnlyList<QuickBooksQueueOutcome> outcomes)
    {
        var queued = outcomes.Where(o => o.Queued).ToList();
        string? queuedText = queued.Count switch
        {
            0 => null,
            1 => $"{queued[0].InvoiceNumber} is on its way to QuickBooks.",
            _ => $"{queued.Count} invoices are on their way to QuickBooks."
        };

        var problems = outcomes
            .Where(o => !o.Queued)
            .Select(o => string.IsNullOrWhiteSpace(o.Reason)
                ? $"{o.InvoiceNumber}: not sent."
                : $"{o.InvoiceNumber}: {o.Reason}")
            .ToList();
        return (queuedText, problems);
    }

    public static void PutOutcomes(ITempDataDictionary tempData, IReadOnlyList<QuickBooksQueueOutcome> outcomes)
    {
        var (queued, problems) = Summarise(outcomes);
        if (queued is not null)
        {
            tempData[MessageKey] = queued;
        }

        if (problems.Count > 0)
        {
            tempData[ProblemsKey] = string.Join("\n", problems);
        }
    }

    /// <summary>"Send to QuickBooks" for one invoice (the invoice and preview pages): queued, or
    /// the reason it wasn't.</summary>
    public static async Task QueueOneAsync(
        QuickBooksSendService sendService, ITempDataDictionary tempData, Guid invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = (await sendService.QueueAsync([invoiceId], cancellationToken)).FirstOrDefault();
            if (outcome is { Queued: true })
            {
                tempData[MessageKey] = "On its way to QuickBooks.";
            }
            else
            {
                tempData[ErrorKey] = outcome?.Reason ?? "This invoice was not sent to QuickBooks.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            tempData[ErrorKey] = ex.Message;
        }
    }

    /// <summary>Runs one QuickBooks action from a page and leaves its result for the redirect:
    /// <paramref name="done"/> when it worked, the service's own words when it was refused.</summary>
    public static async Task RunAsync(ITempDataDictionary tempData, Func<Task> action, string done)
    {
        try
        {
            await action();
            tempData[MessageKey] = done;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            tempData[ErrorKey] = ex.Message;
        }
    }

    /// <summary>The connection state as Setup → QuickBooks reads it. A failure to read it counts as
    /// not connected: the invoice pages must still load.</summary>
    public static async Task<QuickBooksConnectionState> ReadConnectionAsync(
        QuickBooksConnectionService connections, CancellationToken cancellationToken)
    {
        try
        {
            return (await connections.GetViewAsync(cancellationToken)).State;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return QuickBooksConnectionState.NotConnected;
        }
    }

    /// <summary>QuickBooks' figure minus LabelShop's; null until QuickBooks has reported one.</summary>
    public static decimal? Difference(decimal labelShop, decimal? quickBooks) =>
        quickBooks is { } qb ? qb - labelShop : null;
}
