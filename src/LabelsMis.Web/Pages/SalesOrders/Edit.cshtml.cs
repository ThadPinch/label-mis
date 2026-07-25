using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.SalesOrders;
using LabelsMis.Web.Services.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.SalesOrders;

/// <summary>Read-only production spec for a sales order line, resolved from its product.</summary>
public record SalesOrderLineDetail(
    string ProductSku,
    string ProductDescription,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    string SubstrateLabel,
    LabelsMis.Domain.Enums.InkSet InkSet,
    string? DieLabel,
    IReadOnlyList<string> FinishingOperations);

[Authorize(Policy = TransactionPolicies.SalesOrdersRead)]
public class EditModel(
    SalesOrderService salesOrderService,
    JobService jobService,
    ArtworkService artworkService,
    ShippingMethodService shippingMethodService,
    CustomerService customerService,
    LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public SalesOrderPageInput Input { get; set; } = new();
    public Domain.Entities.SalesOrder? Order { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanSchedule { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanCancel { get; private set; }
    public bool IsLocked { get; private set; }
    public IReadOnlyList<LineJobInfo> LineJobs { get; private set; } = [];
    public IReadOnlyList<OrderShipmentInfo> Shipments { get; private set; } = [];
    public IReadOnlyList<OrderInvoiceInfo> Invoices { get; private set; } = [];
    public IReadOnlyList<SalesOrderLineDetail> LineDetails { get; private set; } = [];
    public Guid? SourceEstimateId { get; private set; }
    public string? SourceEstimateNumber { get; private set; }
    public string? SalesRepName { get; private set; }

    public record LineJobInfo(
        int LineNumber,
        string ProductLabel,
        Guid? JobId,
        string? JobNumber,
        JobStatus? JobStatus);

    public record OrderShipmentInfo(
        Guid Id,
        string ShipmentNumber,
        DateOnly ShipDate,
        ShipmentStatus Status);

    public record OrderInvoiceInfo(
        Guid Id,
        string InvoiceNumber,
        DateOnly InvoiceDate,
        InvoiceStatus Status,
        decimal Total,
        decimal BalanceDue);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Order = await salesOrderService.GetAsync(Id, cancellationToken);
        if (Order is null) return NotFound();

        IsLocked = Order.Status != SalesOrderStatus.Open;
        CanEdit = !IsLocked && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Csr) || User.IsInRole(AppRoles.Estimator));
        CanSchedule = Order.Status == SalesOrderStatus.Open
            && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Csr));
        CanDelete = Order.Status == SalesOrderStatus.Open && User.IsInRole(AppRoles.Admin);
        CanCancel = Order.Status is not (SalesOrderStatus.Open or SalesOrderStatus.Cancelled or SalesOrderStatus.Closed);
        Input = ToPageInput(Order);
        SourceEstimateId = Order.SourceEstimateId;
        if (Order.SourceEstimateId is Guid sourceEstimateId)
        {
            SourceEstimateNumber = await db.Estimates.AsNoTracking()
                .Where(e => e.Id == sourceEstimateId)
                .Select(e => e.EstimateNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (Order.SalesRepId is Guid salesRepId)
        {
            SalesRepName = await db.Users.AsNoTracking()
                .Where(u => u.Id == salesRepId)
                .Select(u => u.Email ?? u.UserName)
                .FirstOrDefaultAsync(cancellationToken);
        }
        await LoadLineJobsAsync(cancellationToken);
        await LoadLineDetailsAsync(cancellationToken);
        await LoadRelatedDocumentsAsync(cancellationToken);
        await LoadLookupsAsync(Order.CustomerId, cancellationToken);
        return Page();
    }

    private async Task LoadLineDetailsAsync(CancellationToken cancellationToken)
    {
        if (Order is null)
        {
            return;
        }

        var orderedLines = Order.Lines.OrderBy(l => l.LineNumber).ToList();
        if (orderedLines.Count == 0)
        {
            return;
        }

        var productIds = orderedLines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // The ordered spec is snapshotted on the line. Fall back to the product's template only for
        // pre-refactor rows whose Spec hasn't been backfilled yet.
        var specs = orderedLines
            .Select(l => l.Spec ?? (products.TryGetValue(l.ProductId, out var p) ? p.ToLabelSpec() : null))
            .ToList();

        var substrateIds = specs.Where(x => x is not null).Select(x => x!.SubstrateId).Distinct().ToList();
        var substrateNames = await db.Stocks.AsNoTracking()
            .Where(s => substrateIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => $"{s.Code} — {s.Description}", cancellationToken);
        var dieNames = await db.Dies.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Description, cancellationToken);
        var operationNames = await db.FinishingOperations.AsNoTracking()
            .ToDictionaryAsync(o => o.Id, o => $"{o.Code} — {o.Description}", cancellationToken);
        var stockCodes = await db.Stocks.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);

        var details = new List<SalesOrderLineDetail>();
        for (var i = 0; i < orderedLines.Count; i++)
        {
            var line = orderedLines[i];
            var spec = specs[i];
            products.TryGetValue(line.ProductId, out var product);

            if (spec is null)
            {
                details.Add(new SalesOrderLineDetail(
                    product?.InternalSku ?? "—", line.Description ?? product?.Description ?? "—",
                    0m, 0m, 0m, "—", default, null, []));
                continue;
            }

            var finishing = EstimateCalculationMapper.DeserializeFinishingOperations(spec.FinishingOperationsJson)
                .OrderBy(f => f.SortOrder)
                .Select(f =>
                {
                    var name = operationNames.TryGetValue(f.OperationId, out var n) ? n : "Unknown operation";
                    return f.StockId is { } stockId && stockCodes.TryGetValue(stockId, out var code)
                        ? $"{name} · {code}"
                        : name;
                })
                .ToList();

            details.Add(new SalesOrderLineDetail(
                product?.InternalSku ?? "—",
                line.Description ?? product?.Description ?? "—",
                spec.LabelAcrossIn,
                spec.LabelAroundIn,
                spec.CornerRadiusIn,
                substrateNames.GetValueOrDefault(spec.SubstrateId, "—"),
                spec.InkSet,
                spec.DieId is Guid dieId ? dieNames.GetValueOrDefault(dieId) : null,
                finishing));
        }

        LineDetails = details;
        ViewData["LineDetails"] = details;
    }

    private async Task LoadRelatedDocumentsAsync(CancellationToken cancellationToken)
    {
        Shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrderId == Id)
            .OrderBy(s => s.ShipDate)
            .Select(s => new OrderShipmentInfo(s.Id, s.ShipmentNumber, s.ShipDate, s.Status))
            .ToListAsync(cancellationToken);

        Invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.SalesOrderId == Id)
            .OrderBy(i => i.InvoiceDate)
            .Select(i => new OrderInvoiceInfo(i.Id, i.InvoiceNumber, i.InvoiceDate, i.Status, i.Total, i.BalanceDue))
            .ToListAsync(cancellationToken);
    }

    private async Task LoadLineJobsAsync(CancellationToken cancellationToken)
    {
        if (Order is null)
        {
            return;
        }

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.SalesOrderLine.SalesOrderId == Id)
            .Select(j => new { j.SalesOrderLineId, j.Id, j.JobNumber, j.Status })
            .ToListAsync(cancellationToken);

        LineJobs = Order.Lines.OrderBy(l => l.LineNumber).Select(l =>
        {
            var job = jobs.FirstOrDefault(j => j.SalesOrderLineId == l.Id);
            return new LineJobInfo(
                l.LineNumber,
                $"{l.Product.InternalSku} — {l.Description ?? l.Product.Description}",
                job?.Id,
                job?.JobNumber,
                job?.Status);
        }).ToList();
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        try
        {
            await salesOrderService.CancelAsync(Id, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Order = await salesOrderService.GetAsync(Id, cancellationToken);
            if (Order is null) return NotFound();
            IsLocked = Order.Status != SalesOrderStatus.Open;
            CanEdit = !IsLocked && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Csr) || User.IsInRole(AppRoles.Estimator));
            CanSchedule = Order.Status == SalesOrderStatus.Open
                && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Csr));
            CanDelete = Order.Status == SalesOrderStatus.Open && User.IsInRole(AppRoles.Admin);
            CanCancel = Order.Status is not (SalesOrderStatus.Open or SalesOrderStatus.Cancelled or SalesOrderStatus.Closed);
            Input = ToPageInput(Order);
            await LoadLookupsAsync(Order.CustomerId, cancellationToken);
            return Page();
        }
    }

    private static SalesOrderPageInput ToPageInput(Domain.Entities.SalesOrder order) => new()
    {
        CustomerId = order.CustomerId,
        CustomerPoNumber = order.CustomerPoNumber,
        RequestedShipDate = order.RequestedShipDate,
        Notes = order.Notes,
        ShippingMethodId = order.ShippingMethodId,
        ShippingCost = order.ShippingCost,
        ShipToName = order.ShipToName,
        ShipToStreet1 = order.ShipToStreet1,
        ShipToStreet2 = order.ShipToStreet2,
        ShipToCity = order.ShipToCity,
        ShipToState = order.ShipToState,
        ShipToZip = order.ShipToZip,
        ShipToCountry = order.ShipToCountry,
        Lines = order.Lines.OrderBy(l => l.LineNumber).Select(l => new SalesOrderLinePageInput
        {
            ProductId = l.ProductId,
            SourceEstimateLineId = l.SourceEstimateLineId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            LineNotes = l.LineNotes,
            Description = l.Description,
            HasArtwork = !string.IsNullOrWhiteSpace(l.Product.ArtworkFilePath),
            SpecJson = l.Spec is null ? null : System.Text.Json.JsonSerializer.Serialize(l.Spec)
        }).ToList()
    };

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var admin = User.IsInRole(AppRoles.Admin);
        if (!admin && !User.IsInRole(AppRoles.Csr) && !User.IsInRole(AppRoles.Estimator)) return Forbid();
        await salesOrderService.UpdateAsync(Id, Input.ToForm(), admin, cancellationToken);
        await UploadLineArtworkAsync(cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin)) return Forbid();

        try
        {
            await salesOrderService.DeleteAsync(Id, cancellationToken);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Order = await salesOrderService.GetAsync(Id, cancellationToken);
            if (Order is null) return NotFound();
            IsLocked = Order.Status != SalesOrderStatus.Open;
            CanEdit = !IsLocked && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Csr) || User.IsInRole(AppRoles.Estimator));
            CanSchedule = Order.Status == SalesOrderStatus.Open
                && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Csr));
            CanDelete = Order.Status == SalesOrderStatus.Open && User.IsInRole(AppRoles.Admin);
            Input = ToPageInput(Order);
            await LoadLookupsAsync(Order.CustomerId, cancellationToken);
            return Page();
        }
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
            return RedirectToPage("/Production/PrePress");
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

    public Task<IActionResult> OnGetAddressesAsync(Guid? customerId, CancellationToken cancellationToken) =>
        ShipToAddressJson.BuildAsync(customerService, customerId, cancellationToken);

    private async Task LoadLookupsAsync(Guid customerId, CancellationToken cancellationToken)
    {
        ViewData["ShippingMethods"] = await shippingMethodService.GetSelectableAsync(
            Input.ShippingMethodId, cancellationToken);
        ViewData["Customers"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Products"] = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.CustomerAssignments.Any(a => a.CustomerId == customerId))
            .OrderBy(p => p.InternalSku)
            .Select(p => new SelectListItem($"{p.InternalSku} — {p.Description}", p.Id.ToString()))
            .ToListAsync(cancellationToken);
    }
}
