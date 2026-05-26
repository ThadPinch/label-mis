using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.SalesOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.SalesOrders;

[Authorize(Policy = TransactionPolicies.SalesOrdersRead)]
public class EditModel(
    SalesOrderService salesOrderService,
    JobService jobService,
    ArtworkService artworkService,
    LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public SalesOrderPageInput Input { get; set; } = new();
    public Domain.Entities.SalesOrder? Order { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanSchedule { get; private set; }
    public bool IsLocked { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Order = await salesOrderService.GetAsync(Id, cancellationToken);
        if (Order is null) return NotFound();

        IsLocked = Order.Status != SalesOrderStatus.Open;
        CanEdit = !IsLocked && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Csr) || User.IsInRole(AppRoles.Estimator));
        CanSchedule = Order.Status == SalesOrderStatus.Open
            && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Csr));
        Input = new SalesOrderPageInput
        {
            CustomerId = Order.CustomerId,
            CustomerPoNumber = Order.CustomerPoNumber,
            RequestedShipDate = Order.RequestedShipDate,
            Notes = Order.Notes,
            Lines = Order.Lines.OrderBy(l => l.LineNumber).Select(l => new SalesOrderLinePageInput
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                LineNotes = l.LineNotes,
                HasArtwork = !string.IsNullOrWhiteSpace(l.Product.ArtworkFilePath)
            }).ToList()
        };
        await LoadLookupsAsync(Order.CustomerId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var admin = User.IsInRole(AppRoles.Admin);
        if (!admin && !User.IsInRole(AppRoles.Csr) && !User.IsInRole(AppRoles.Estimator)) return Forbid();
        await salesOrderService.UpdateAsync(Id, Input.ToForm(), admin, cancellationToken);
        await UploadLineArtworkAsync(cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    private async Task UploadLineArtworkAsync(CancellationToken cancellationToken)
    {
        foreach (var line in Input.Lines.Where(l => l.ProductId != Guid.Empty && l.ArtworkFile is { Length: > 0 }))
        {
            await artworkService.UploadForProductAsync(line.ProductId, line.ArtworkFile!, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostScheduleForProductionAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Scheduler) && !User.IsInRole(AppRoles.Csr))
        {
            return Forbid();
        }

        try
        {
            var jobs = await jobService.ScheduleFromSalesOrderAsync(Id, cancellationToken);
            return RedirectToPage("/Jobs/Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Order = await salesOrderService.GetAsync(Id, cancellationToken);
            IsLocked = Order!.Status != SalesOrderStatus.Open;
            CanEdit = false;
            CanSchedule = false;
            await LoadLookupsAsync(Order.CustomerId, cancellationToken);
            return Page();
        }
    }

    private async Task LoadLookupsAsync(Guid customerId, CancellationToken cancellationToken)
    {
        ViewData["Customers"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Products"] = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.CustomerAssignments.Any(a => a.CustomerId == customerId))
            .OrderBy(p => p.InternalSku)
            .Select(p => new SelectListItem($"{p.InternalSku} — {p.Description}", p.Id.ToString()))
            .ToListAsync(cancellationToken);
    }
}
