using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Invoices;

[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class IndexModel(
    InvoiceService invoiceService,
    QuickBooksSendService quickBooksSend,
    QuickBooksConnectionService quickBooksConnection,
    LabelsMisDbContext db) : PageModel
{
    public const string ReadyTab = "ready";
    public const string WaitingTab = "waiting";
    public const string AttentionTab = "attention";
    public const string ExportedTab = "exported";
    private const int PageSize = 25;

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public InvoiceStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ToDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? AgingBucket { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = ReadyTab;
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    public PagedResult<InvoiceListItem> Result { get; private set; } = null!;
    public int ReadyCount { get; private set; }
    public int WaitingCount { get; private set; }
    public int AttentionCount { get; private set; }
    public int ExportedCount { get; private set; }
    public bool OnExportedTab => Tab == ExportedTab;
    public bool OnWaitingTab => Tab == WaitingTab;
    public bool OnAttentionTab => Tab == AttentionTab;
    public bool OnReadyTab => !OnExportedTab && !OnWaitingTab && !OnAttentionTab;

    /// <summary>Why each unexported invoice on the Waiting tab isn't ready for QuickBooks.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> WaitingReasons { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<string>>();
    public bool CanEdit => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);

    public QuickBooksConnectionState QuickBooksConnection { get; private set; }
    public bool QuickBooksConnected => QuickBooksConnection == QuickBooksConnectionState.Connected;

    /// <summary>The QuickBooks state of each invoice on this page of the list.</summary>
    public IReadOnlyDictionary<Guid, QuickBooksSendState> QuickBooksStates { get; private set; } =
        new Dictionary<Guid, QuickBooksSendState>();

    /// <summary>The QuickBooks column shows once there is something to show in it.</summary>
    public bool ShowQuickBooksColumn =>
        QuickBooksConnected || OnAttentionTab || QuickBooksStates.Values.Any(s => s.Label is not null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Ready = unexported, billable, nothing waiting and not stopped on its way to QuickBooks;
        // Waiting = the rest of the unexported set; Needs attention = whatever QuickBooks turned
        // down, exported or not.
        var waiting = await invoiceService.GetWaitingReasonsAsync(cancellationToken);
        WaitingReasons = waiting;
        var attention = await db.Invoices.AsNoTracking()
            .Where(i => i.QbExportStatus == QuickBooksExportStatus.NeedsAttention)
            .Select(i => new { i.Id, i.Status, i.QbExportedAt, i.NonBillable })
            .ToListAsync(cancellationToken);

        Result = OnAttentionTab
            ? await ListNeedsAttentionAsync(cancellationToken)
            : await invoiceService.ListAsync(
                Search, Status, CustomerId, FromDate, ToDate, AgingBucket, OnExportedTab, Sort, PageNumber, PageSize, cancellationToken,
                onlyIds: OnWaitingTab ? waiting.Keys.ToList() : null,
                excludeIds: OnReadyTab ? waiting.Keys.Concat(attention.Select(a => a.Id)).ToList() : null);

        (ReadyCount, WaitingCount, ExportedCount) = await invoiceService.GetExportCountsAsync(waiting, cancellationToken);
        ReadyCount -= attention.Count(a =>
            a.Status != InvoiceStatus.Void && a.QbExportedAt == null && !a.NonBillable && !waiting.ContainsKey(a.Id));
        AttentionCount = attention.Count;

        QuickBooksConnection = await QuickBooksUi.ReadConnectionAsync(quickBooksConnection, cancellationToken);
        if (Result.Items.Count > 0)
        {
            QuickBooksStates = (await quickBooksSend.GetStatesAsync(Result.Items.Select(i => i.Id).ToList(), cancellationToken))
                .ToDictionary(s => s.InvoiceId);
        }

        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }

    /// <summary>The Needs attention tab. Void invoices count too (a void QuickBooks refused), which
    /// the shared invoice list leaves out, so the tab reads its own short list.</summary>
    private async Task<PagedResult<InvoiceListItem>> ListNeedsAttentionAsync(CancellationToken cancellationToken)
    {
        var query = db.Invoices.AsNoTracking()
            .Where(i => i.QbExportStatus == QuickBooksExportStatus.NeedsAttention);
        if (CustomerId is { } customerId)
        {
            query = query.Where(i => i.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim().ToUpperInvariant();
            query = query.Where(i => i.InvoiceNumber.ToUpper().Contains(term) || i.Customer.Name.ToUpper().Contains(term));
        }

        var (sortKey, desc) = QueryExtensions.ParseSort(Sort);
        query = sortKey switch
        {
            "number" => query.OrderByDir(desc, i => i.InvoiceNumber),
            "customer" => query.OrderByDir(desc, i => i.Customer.Name),
            "order" => query.OrderByDir(desc, i => i.SalesOrder.OrderNumber),
            "date" => query.OrderByDir(desc, i => i.InvoiceDate),
            "due" => query.OrderByDir(desc, i => i.DueDate),
            "status" => query.OrderByDir(desc, i => i.Status),
            "total" => query.OrderByDir(desc, i => i.Total),
            "balance" => query.OrderByDir(desc, i => i.BalanceDue),
            _ => query.OrderByDescending(i => i.QbLastAttemptAt).ThenBy(i => i.InvoiceNumber)
        };

        var total = await query.CountAsync(cancellationToken);
        var page = Math.Clamp(PageNumber, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        var rows = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(i => new InvoiceListItem(
                i.Id, i.InvoiceNumber, i.Customer.Name, i.Status, i.InvoiceDate, i.DueDate, i.Total, i.BalanceDue,
                i.QbExportedAt, null, 0, 0, i.SalesOrderId, i.SalesOrder.OrderNumber, i.NonBillable, i.NonBillableReason))
            .ToListAsync(cancellationToken);
        return new PagedResult<InvoiceListItem>(rows, page, PageSize, total);
    }

    /// <summary>Puts the ticked Ready invoices in line for QuickBooks; the flash message says
    /// which went and why any didn't.</summary>
    public async Task<IActionResult> OnPostSendSelectedAsync(List<Guid> selectedIds, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        if (selectedIds.Count > 0)
        {
            try
            {
                var outcomes = await quickBooksSend.QueueAsync(selectedIds.Distinct().ToList(), cancellationToken);
                QuickBooksUi.PutOutcomes(TempData, outcomes);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                TempData[QuickBooksUi.ErrorKey] = ex.Message;
            }
        }

        return RedirectToPage(FilterRouteValues());
    }

    /// <summary>Needs attention → Retry: back in line for QuickBooks.</summary>
    public async Task<IActionResult> OnPostQuickBooksRetryAsync(Guid id, string? invoiceNumber, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        await QuickBooksUi.RunAsync(TempData, () => quickBooksSend.RetryAsync(id, cancellationToken),
            $"{invoiceNumber ?? "The invoice"} is back in line for QuickBooks.");
        return RedirectToPage(FilterRouteValues());
    }

    /// <summary>Needs attention → Settled by hand: the problem was dealt with in QuickBooks itself.</summary>
    public async Task<IActionResult> OnPostQuickBooksCancelAsync(Guid id, string? invoiceNumber, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        await QuickBooksUi.RunAsync(TempData, () => quickBooksSend.CancelAsync(id, cancellationToken),
            $"{invoiceNumber ?? "The invoice"} is off the Needs attention list.");
        return RedirectToPage(FilterRouteValues());
    }

    public async Task<IActionResult> OnPostMarkSelectedExportedAsync(List<Guid> selectedIds, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        if (selectedIds.Count > 0)
        {
            var marked = await invoiceService.MarkExportedAsync(selectedIds, cancellationToken);
            var skipped = selectedIds.Distinct().Count() - marked;
            TempData["ExportMessage"] = (marked == 1
                ? "1 invoice marked as exported."
                : $"{marked} invoices marked as exported.")
                + (skipped > 0 ? $" {skipped} skipped (not on the Ready list)." : string.Empty);
        }

        return RedirectToPage(FilterRouteValues());
    }

    public async Task<IActionResult> OnPostExportSelectedAsync(List<Guid> selectedIds, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        if (selectedIds.Count == 0)
        {
            return RedirectToPage(FilterRouteValues());
        }

        var (content, fileName, count) = await invoiceService.ExportSelectedCsvAsync(selectedIds, cancellationToken);
        if (count == 0)
        {
            return RedirectToPage(FilterRouteValues());
        }

        return File(content, "text/csv", fileName);
    }

    public async Task<IActionResult> OnPostUnmarkExportedAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        await invoiceService.UnmarkExportedAsync(id, cancellationToken);
        return RedirectToPage(FilterRouteValues());
    }

    /// <summary>Current filters + tab, so tab links and post-action redirects keep the same view.
    /// Switching tabs resets the page number.</summary>
    public Dictionary<string, string> FilterRouteValues(string? tab = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Search"] = Search,
            ["Status"] = Status?.ToString(),
            ["CustomerId"] = CustomerId?.ToString(),
            ["FromDate"] = FromDate?.ToString("yyyy-MM-dd"),
            ["ToDate"] = ToDate?.ToString("yyyy-MM-dd"),
            ["AgingBucket"] = AgingBucket,
            ["Sort"] = Sort,
            ["Tab"] = tab ?? Tab,
            ["pageNumber"] = tab is null && PageNumber > 1 ? PageNumber.ToString() : null
        };

        return values.Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value!);
    }
}
