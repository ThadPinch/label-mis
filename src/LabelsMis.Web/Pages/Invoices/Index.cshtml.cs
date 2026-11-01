using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Invoices;

[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class IndexModel(InvoiceService invoiceService, LabelsMisDbContext db) : PageModel
{
    public const string ReadyTab = "ready";
    public const string WaitingTab = "waiting";
    public const string ExportedTab = "exported";

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public InvoiceStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ToDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? AgingBucket { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = ReadyTab;
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<InvoiceListItem> Result { get; private set; } = null!;
    public int ReadyCount { get; private set; }
    public int WaitingCount { get; private set; }
    public int ExportedCount { get; private set; }
    public bool OnExportedTab => Tab == ExportedTab;
    public bool OnWaitingTab => Tab == WaitingTab;
    public bool OnReadyTab => !OnExportedTab && !OnWaitingTab;

    /// <summary>Why each unexported invoice on the Waiting tab isn't ready for QuickBooks.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> WaitingReasons { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<string>>();
    public bool CanEdit => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Ready = unexported, billable and nothing waiting; Waiting = the rest of the unexported set.
        var waiting = await invoiceService.GetWaitingReasonsAsync(cancellationToken);
        WaitingReasons = waiting;
        Result = await invoiceService.ListAsync(
            Search, Status, CustomerId, FromDate, ToDate, AgingBucket, OnExportedTab, Sort, PageNumber, 25, cancellationToken,
            onlyIds: OnWaitingTab ? waiting.Keys.ToList() : null,
            excludeIds: OnReadyTab ? waiting.Keys.ToList() : null);

        (ReadyCount, WaitingCount, ExportedCount) = await invoiceService.GetExportCountsAsync(waiting, cancellationToken);

        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
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
