using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Invoices;
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
    SalesOrderDocumentService documentService,
    ShippingMethodService shippingMethodService,
    CustomerService customerService,
    InvoiceService invoiceService,
    LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Unlocked { get; set; }
    [BindProperty] public SalesOrderPageInput Input { get; set; } = new();
    public Domain.Entities.SalesOrder? Order { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanSchedule { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanCancel { get; private set; }
    public bool IsLocked { get; private set; }

    /// <summary>Admins and CSRs may unlock an in-production order to edit it.</summary>
    public bool CanUnlock => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Csr);

    public bool CanSendInvoice => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Csr) || User.IsInRole(AppRoles.Accounting);

    /// <summary>Supporting documents may be added/removed in any order status — they arrive throughout the order's life.</summary>
    public bool CanManageDocuments => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Csr) || User.IsInRole(AppRoles.Estimator);

    /// <summary>The form is editable: the order is open, or it was explicitly unlocked.</summary>
    public bool IsEditing => !IsLocked || (Unlocked && CanUnlock);
    // Invoice email modal fields.
    [BindProperty] public Guid SendInvoiceId { get; set; }
    [BindProperty] public string? EmailTo { get; set; }
    [BindProperty] public string? EmailSubject { get; set; }
    [BindProperty] public string? EmailBody { get; set; }
    [BindProperty] public bool IncludePdf { get; set; } = true;

    /// <summary>Customer payment terms; Prepay gates scheduling on full payment.</summary>
    public PaymentTerms? CustomerTerms { get; private set; }
    public bool RequiresPrepay => CustomerTerms == PaymentTerms.Prepay;

    /// <summary>Sum of payments recorded against the order's non-void invoices.</summary>
    public decimal AmountPaid { get; private set; }
    public decimal OrderTotal { get; private set; }
    public bool PrepaySatisfied => AmountPaid >= OrderTotal;

    public IReadOnlyList<(string Name, string Email)> CustomerContacts { get; private set; } = [];
    public string? DefaultInvoiceEmail { get; private set; }

    public IReadOnlyList<LineJobInfo> LineJobs { get; private set; } = [];
    public IReadOnlyList<OrderShipmentInfo> Shipments { get; private set; } = [];
    public IReadOnlyList<OrderInvoiceInfo> Invoices { get; private set; } = [];
    public IReadOnlyList<OrderDocumentInfo> Documents { get; private set; } = [];
    [BindProperty] public IFormFile? DocumentFile { get; set; }
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

    public record OrderDocumentInfo(
        Guid Id,
        string FileName,
        long SizeBytes,
        DateTime UploadedAt);

    public record OrderInvoiceInfo(
        Guid Id,
        string InvoiceNumber,
        DateOnly InvoiceDate,
        InvoiceStatus Status,
        decimal Total,
        decimal BalanceDue,
        DateTime? QbExportedAt);

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
        ApplyLineJobsToInput();
        await LoadLineDetailsAsync(cancellationToken);
        await LoadRelatedDocumentsAsync(cancellationToken);
        await LoadLookupsAsync(Order.CustomerId, cancellationToken);
        ViewData["IsLocked"] = !IsEditing;
        return Page();
    }

    /// <summary>Marks each line input with its existing job so the form can pin the product select.</summary>
    private void ApplyLineJobsToInput()
    {
        foreach (var lineJob in LineJobs)
        {
            var line = Input.Lines.ElementAtOrDefault(lineJob.LineNumber - 1);
            if (line is not null && lineJob.JobId is not null)
            {
                line.HasJob = true;
                line.JobNumber = lineJob.JobNumber;
            }
        }
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
            .Select(i => new OrderInvoiceInfo(i.Id, i.InvoiceNumber, i.InvoiceDate, i.Status, i.Total, i.BalanceDue, i.QbExportedAt))
            .ToListAsync(cancellationToken);

        Documents = await db.SalesOrderDocuments.AsNoTracking()
            .Where(d => d.SalesOrderId == Id)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new OrderDocumentInfo(d.Id, d.OriginalFileName, d.FileSizeBytes, d.CreatedAt))
            .ToListAsync(cancellationToken);

        AmountPaid = Invoices
            .Where(i => i.Status != InvoiceStatus.Void)
            .Sum(i => i.Total - i.BalanceDue);
        OrderTotal = Order is null
            ? 0m
            : Order.Lines.Sum(l => l.LineTotal) + Order.Charges.Sum(c => c.LineTotal) + Order.ShippingCost;

        if (Order is not null)
        {
            var customer = await customerService.GetByIdAsync(Order.CustomerId, cancellationToken);
            CustomerTerms = customer?.Terms;
            CustomerContacts = (customer?.Contacts ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Email))
                .OrderByDescending(c => c.IsPrimary)
                .Select(c => (c.FullName, c.Email!))
                .ToList();
            DefaultInvoiceEmail = CustomerContacts.Select(c => c.Email).FirstOrDefault();
        }
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
        BillingNotes = order.BillingNotes,
        ShippingMethodId = order.ShippingMethodId,
        ShippingCost = order.ShippingCost,
        ShipToName = order.ShipToName,
        ShipToStreet1 = order.ShipToStreet1,
        ShipToStreet2 = order.ShipToStreet2,
        ShipToCity = order.ShipToCity,
        ShipToState = order.ShipToState,
        ShipToZip = order.ShipToZip,
        ShipToCountry = order.ShipToCountry,
        Charges = order.Charges.OrderBy(c => c.LineNumber).Select(c => new SalesOrderChargePageInput
        {
            Id = c.Id,
            SourceEstimateChargeId = c.SourceEstimateChargeId,
            Description = c.Description,
            Quantity = c.Quantity,
            UnitPrice = c.UnitPrice
        }).ToList(),
        Lines = order.Lines.OrderBy(l => l.LineNumber).Select(l =>
        {
            var spec = l.Spec;
            return new SalesOrderLinePageInput
            {
                Id = l.Id,
                ProductId = l.ProductId,
                SourceEstimateLineId = l.SourceEstimateLineId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                LineNotes = l.LineNotes,
                Description = l.Description,
                Unwind = spec?.Unwind,
                HasArtwork = !string.IsNullOrWhiteSpace(l.Product.ArtworkFilePath),
                ArtworkFileName = string.IsNullOrWhiteSpace(l.Product.ArtworkFilePath)
                    ? null
                    : l.Product.ArtworkOriginalFileName ?? Path.GetFileName(l.Product.ArtworkFilePath),
                SpecJson = spec is null ? null : System.Text.Json.JsonSerializer.Serialize(spec),
                LabelAcrossIn = spec?.LabelAcrossIn,
                LabelAroundIn = spec?.LabelAroundIn,
                CornerRadiusIn = spec?.CornerRadiusIn,
                GutterAcrossIn = spec?.GutterAcrossIn,
                GutterAroundIn = spec?.GutterAroundIn,
                BleedIn = spec?.BleedIn,
                SubstrateId = spec?.SubstrateId,
                DieId = spec?.DieId,
                ShrinkLayflatIn = spec?.ShrinkLayflatIn,
                InkSet = spec?.InkSet,
                WhiteHits = spec?.WhiteHits,
                WhiteCoveragePct = spec is null ? null : spec.WhiteCoveragePct * 100m,
                SetupWasteImpressions = spec?.SetupWasteImpressions,
                RunningWastePct = spec?.RunningWastePct,
                MaxLabelsAcrossOverride = spec?.MaxLabelsAcrossOverride,
                LabelOrientationOverride = spec?.LabelOrientationOverride,
                Spots = spec is null
                    ? []
                    : EstimateCalculationMapper.DeserializeSpots(spec.SpotsJson)
                        .OrderBy(s => s.SortOrder)
                        .Select(s => new OrderSpotPageInput { InkId = s.InkId, Hits = s.Hits, CoveragePct = s.CoveragePct * 100m })
                        .ToList(),
                Finishing = spec is null
                    ? []
                    : EstimateCalculationMapper.DeserializeFinishingOperations(spec.FinishingOperationsJson)
                        .OrderBy(f => f.SortOrder)
                        .Select(f => new OrderFinishingPageInput
                        {
                            OperationId = f.OperationId,
                            Include = true,
                            StockId = f.StockId,
                            DieId = f.DieId,
                            SetupMinutesOverride = f.SetupMinutesOverride,
                            RunSpeedFpmOverride = f.RunSpeedFpmOverride
                        })
                        .ToList()
            };
        }).ToList()
    };

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var admin = User.IsInRole(AppRoles.Admin);
        if (!admin && !User.IsInRole(AppRoles.Csr) && !User.IsInRole(AppRoles.Estimator)) return Forbid();

        try
        {
            var status = await db.SalesOrders.AsNoTracking()
                .Where(o => o.Id == Id)
                .Select(o => o.Status)
                .SingleAsync(cancellationToken);

            if (status == SalesOrderStatus.Open)
            {
                await salesOrderService.UpdateAsync(Id, Input.ToForm(), admin, cancellationToken);
            }
            else
            {
                // Unlocked edit of an in-production order: in-place line updates + job propagation.
                if (!CanUnlock) return Forbid();
                await salesOrderService.UpdateInProductionAsync(Id, Input.ToForm(), cancellationToken);
            }

            await UploadLineArtworkAsync(cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await OnGetAsync(cancellationToken);
        }
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

    public async Task<IActionResult> OnPostScheduleForProductionAsync(bool force, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Scheduler) && !User.IsInRole(AppRoles.Csr))
        {
            return Forbid();
        }

        try
        {
            // Prepay customers must have the order paid in full before it can be scheduled,
            // unless the user explicitly forces it.
            if (!force && await IsPrepayOutstandingAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "This customer has Prepay terms and the order is not paid in full. " +
                    "Record the payment on the invoice, or use Force to production.");
            }

            var jobs = await jobService.ScheduleFromSalesOrderAsync(Id, cancellationToken);
            return RedirectToPage("/Production/PrePress");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await OnGetAsync(cancellationToken);
        }
    }

    /// <summary>True when the customer is on Prepay terms and payments do not yet cover the order total.</summary>
    private async Task<bool> IsPrepayOutstandingAsync(CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .Include(o => o.Charges)
            .SingleAsync(o => o.Id == Id, cancellationToken);
        if (order.Customer.Terms != PaymentTerms.Prepay)
        {
            return false;
        }

        var orderTotal = order.Lines.Sum(l => l.LineTotal) + order.Charges.Sum(c => c.LineTotal) + order.ShippingCost;
        var paid = await db.Invoices.AsNoTracking()
            .Where(i => i.SalesOrderId == Id && i.Status != InvoiceStatus.Void)
            .SumAsync(i => i.Total - i.BalanceDue, cancellationToken);
        return paid < orderTotal;
    }

    public async Task<IActionResult> OnPostSendInvoiceAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Csr) && !User.IsInRole(AppRoles.Accounting))
        {
            return Forbid();
        }

        try
        {
            var belongsToOrder = await db.Invoices.AsNoTracking()
                .AnyAsync(i => i.Id == SendInvoiceId && i.SalesOrderId == Id, cancellationToken);
            if (!belongsToOrder)
            {
                throw new InvalidOperationException("Invoice not found on this order.");
            }

            await invoiceService.SendAsync(SendInvoiceId, EmailTo, EmailSubject, EmailBody, IncludePdf, cancellationToken);
            TempData["OrderStatus"] = $"Invoice emailed to {EmailTo}.";
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            TempData["OrderError"] = $"Could not send the invoice: {ex.Message}";
            return RedirectToPage(new { id = Id });
        }
    }

    public async Task<IActionResult> OnPostUploadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!CanManageDocuments) return Forbid();

        if (DocumentFile is null || DocumentFile.Length == 0)
        {
            TempData["OrderError"] = "Choose a file to upload.";
            return RedirectToPage(new { id = Id, unlocked = Unlocked ? "true" : null });
        }

        try
        {
            await documentService.UploadAsync(Id, DocumentFile, cancellationToken);
            TempData["OrderStatus"] = $"Uploaded {DocumentFile.FileName}.";
        }
        catch (Exception ex)
        {
            TempData["OrderError"] = $"Could not upload the document: {ex.Message}";
        }

        return RedirectToPage(new { id = Id, unlocked = Unlocked ? "true" : null });
    }

    public async Task<IActionResult> OnGetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var file = await documentService.OpenAsync(Id, documentId, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        return File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (!CanManageDocuments) return Forbid();

        try
        {
            await documentService.DeleteAsync(Id, documentId, cancellationToken);
            TempData["OrderStatus"] = "Document deleted.";
        }
        catch (Exception ex)
        {
            TempData["OrderError"] = $"Could not delete the document: {ex.Message}";
        }

        return RedirectToPage(new { id = Id, unlocked = Unlocked ? "true" : null });
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

        // Spec editor lookups, matching what the estimate form offers.
        ViewData["Substrates"] = await db.Stocks.AsNoTracking()
            .Where(s => s.IsActive && (s.StockType == StockType.Substrate || s.StockType == StockType.Shrink))
            .OrderBy(s => s.Code)
            .Select(s => new SelectListItem($"{s.Code} — {s.Description}", s.Id.ToString()))
            .ToListAsync(cancellationToken);

        ViewData["ShrinkStocks"] = await db.Stocks.AsNoTracking()
            .Where(s => s.StockType == StockType.Shrink)
            .ToDictionaryAsync(s => s.Id, s => s.ShrinkLayflatIn, cancellationToken);
        ViewData["Dies"] = await db.Dies.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Description)
            .ToListAsync(cancellationToken);
        ViewData["SpotInks"] = await db.Inks.AsNoTracking()
            .Where(i => i.IsActive && i.IsSpot && i.SpotColor != SpotColor.White)
            .OrderBy(i => i.Code)
            .Select(i => new SelectListItem($"{i.Code} — {i.Description}", i.Id.ToString()))
            .ToListAsync(cancellationToken);
        ViewData["FinishingOperations"] = await db.FinishingOperations.AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.Code)
            .ToListAsync(cancellationToken);
        ViewData["FinishingStocks"] = await db.Stocks.AsNoTracking()
            .Where(s => s.IsActive && s.StockType == StockType.Laminate)
            .OrderBy(s => s.Code)
            .Select(s => new SelectListItem($"{s.Code} — {s.Description}", s.Id.ToString()))
            .ToListAsync(cancellationToken);
    }
}
