using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.SalesOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.SalesOrders;

[Authorize(Policy = TransactionPolicies.SalesOrdersRead)]
public class IndexModel(SalesOrderService salesOrderService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public SalesOrderStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ShipFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ShipTo { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<SalesOrderListItem> Result { get; private set; } = null!;
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanCancel { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            return Forbid();
        }

        try
        {
            await salesOrderService.DeleteAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectPreservingFilters();
    }

    /// <summary>Copies the order into a fresh Open order (a reorder) and lands on it.</summary>
    public async Task<IActionResult> OnPostCopyAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Csr) && !User.IsInRole(AppRoles.Estimator))
        {
            return Forbid();
        }

        try
        {
            var sourceNumber = await db.SalesOrders.AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => o.OrderNumber)
                .SingleAsync(cancellationToken);
            var copy = await salesOrderService.CopyAsync(id, cancellationToken);
            TempData["OrderStatus"] = $"{copy.OrderNumber} created as a copy of {sourceNumber}. "
                + "Review the PO number and requested ship date before scheduling.";
            return RedirectToPage("Edit", new { id = copy.Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            return Forbid();
        }

        try
        {
            await salesOrderService.CancelAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectPreservingFilters();
    }

    // Redirect back preserving filters; "page" is a reserved route token so build the query by hand.
    private IActionResult RedirectPreservingFilters()
    {
        var query = QueryString.Empty;
        if (!string.IsNullOrWhiteSpace(Search)) query = query.Add("Search", Search);
        if (Status.HasValue) query = query.Add("Status", Status.Value.ToString());
        if (CustomerId.HasValue) query = query.Add("CustomerId", CustomerId.Value.ToString());
        if (ShipFrom.HasValue) query = query.Add("ShipFrom", ShipFrom.Value.ToString("yyyy-MM-dd"));
        if (ShipTo.HasValue) query = query.Add("ShipTo", ShipTo.Value.ToString("yyyy-MM-dd"));
        if (!string.IsNullOrWhiteSpace(Sort)) query = query.Add("Sort", Sort);
        if (PageNumber > 1) query = query.Add("pageNumber", PageNumber.ToString());
        return Redirect($"{Request.Path}{query}");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin)
            || User.IsInRole(AppRoles.Csr)
            || User.IsInRole(AppRoles.Estimator);
        CanDelete = User.IsInRole(AppRoles.Admin);
        CanCancel = User.IsInRole(AppRoles.Admin);
        Result = await salesOrderService.ListAsync(Search, Status, CustomerId, ShipFrom, ShipTo, Sort, PageNumber, 25, cancellationToken);
        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }
}
