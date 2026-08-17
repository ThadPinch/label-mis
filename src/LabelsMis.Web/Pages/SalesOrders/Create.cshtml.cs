using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.Products;
using LabelsMis.Web.Services.Outsourcing;
using LabelsMis.Web.Services.SalesOrders;
using LabelsMis.Web.Services.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.SalesOrders;

public class SalesOrderPageInput
{
    [Required] public Guid CustomerId { get; set; }
    public string? CustomerPoNumber { get; set; }
    public DateOnly? RequestedShipDate { get; set; }
    public string? Notes { get; set; }
    [StringLength(4000)] public string? BillingNotes { get; set; }
    public List<SalesOrderLinePageInput> Lines { get; set; } = [new()];
    public List<SalesOrderChargePageInput> Charges { get; set; } = [];

    public Guid? ShippingMethodId { get; set; }
    [Range(0, 999999)] public decimal ShippingCost { get; set; }
    [StringLength(200)] public string? ShipToName { get; set; }
    [StringLength(200)] public string? ShipToStreet1 { get; set; }
    [StringLength(200)] public string? ShipToStreet2 { get; set; }
    [StringLength(100)] public string? ShipToCity { get; set; }
    [StringLength(100)] public string? ShipToState { get; set; }
    [StringLength(20)] public string? ShipToZip { get; set; }
    [StringLength(2)] public string? ShipToCountry { get; set; }

    public SalesOrderFormInput ToForm() => new(
        CustomerId, CustomerPoNumber, RequestedShipDate, Notes,
        Lines.Select(l => new SalesOrderLineInput(
            l.Id, l.ProductId, l.SourceEstimateLineId, l.Quantity, l.UnitPrice, l.LineNotes, l.Description, l.BuildSpec(), l.ToOutsource())).ToList(),
        ShippingMethodId,
        ShippingCost,
        new ShippingAddress(ShipToName, ShipToStreet1, ShipToStreet2, ShipToCity, ShipToState, ShipToZip, ShipToCountry),
        BillingNotes,
        Charges
            .Where(c => !string.IsNullOrWhiteSpace(c.Description))
            .Select(c => new SalesOrderChargeInput(c.Id, c.Description!, c.Quantity, c.UnitPrice, c.SourceEstimateChargeId, c.ToOutsource()))
            .ToList());
}

/// <summary>Outsourcing fields shared by order lines and charges on the order form.</summary>
public interface IOutsourcePageInput
{
    bool IsOutsourced { get; }
    Guid? OutsourceVendorId { get; }
    string? OutsourceQuoteNumber { get; }
    decimal? OutsourceCost { get; }
    DateOnly? OutsourceExpectedIn { get; }
    string? OutsourcePrivateNotes { get; }
}

public static class OutsourcePageInputExtensions
{
    public static OutsourceItemInput? ToOutsource(this IOutsourcePageInput input) => input.IsOutsourced
        ? new OutsourceItemInput(
            new OutsourceDetails(input.OutsourceVendorId, input.OutsourceQuoteNumber, input.OutsourceExpectedIn, input.OutsourcePrivateNotes),
            input.OutsourceCost)
        : null;
}

/// <summary>A flat, non-label row on the order form: a one-time charge (die creation, design time)
/// or an outsourced item (promo, print, wide format) bought from a vendor.</summary>
public class SalesOrderChargePageInput : IOutsourcePageInput
{
    public Guid? Id { get; set; }
    public Guid? SourceEstimateChargeId { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(1, 1000000)]
    public int Quantity { get; set; } = 1;

    [Range(0, 1000000)]
    public decimal UnitPrice { get; set; }

    public bool IsOutsourced { get; set; }
    public Guid? OutsourceVendorId { get; set; }
    [StringLength(100)] public string? OutsourceQuoteNumber { get; set; }
    [Range(0, 100000000)] public decimal? OutsourceCost { get; set; }
    [DataType(DataType.Date)] public DateOnly? OutsourceExpectedIn { get; set; }
    [StringLength(2000)] public string? OutsourcePrivateNotes { get; set; }

    /// <summary>Display-only: vendor tracking on an existing order (sent / received), for the row badge.</summary>
    public string? OutsourceStatusLabel { get; set; }
    public bool OutsourceLocked { get; set; }
}

public class OrderSpotPageInput
{
    public Guid InkId { get; set; }
    [Range(1, 3)] public int Hits { get; set; } = 1;
    [Range(0, 100)] public decimal CoveragePct { get; set; } = 100m;
}

public class OrderFinishingPageInput
{
    public Guid OperationId { get; set; }
    public bool Include { get; set; }
    public Guid? StockId { get; set; }
    public Guid? DieId { get; set; }
    public decimal? SetupMinutesOverride { get; set; }
    public decimal? RunSpeedFpmOverride { get; set; }
}

public class SalesOrderLinePageInput : IOutsourcePageInput
{
    /// <summary>Existing line id, round-tripped so saves update lines in place (jobs reference them).</summary>
    public Guid? Id { get; set; }

    /// <summary>Bought from an outside vendor instead of run in-house; the job (if any) is routed to
    /// the vendor and moves to ready-to-ship on receipt.</summary>
    public bool IsOutsourced { get; set; }
    public Guid? OutsourceVendorId { get; set; }
    [StringLength(100)] public string? OutsourceQuoteNumber { get; set; }
    /// <summary>The vendor's cost for the whole line (all units).</summary>
    [Range(0, 100000000)] public decimal? OutsourceCost { get; set; }
    [DataType(DataType.Date)] public DateOnly? OutsourceExpectedIn { get; set; }
    [StringLength(2000)] public string? OutsourcePrivateNotes { get; set; }

    /// <summary>Display-only: vendor tracking on an existing order (sent / received), for the row badge.</summary>
    public string? OutsourceStatusLabel { get; set; }
    /// <summary>Display-only: the outsource switch can no longer be flipped (job exists / vendor involved).</summary>
    public bool OutsourceLocked { get; set; }

    public Guid ProductId { get; set; }
    public Guid? SourceEstimateLineId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; } = 1000;
    [Range(0, double.MaxValue)] public decimal UnitPrice { get; set; }
    public string? LineNotes { get; set; }
    public UnwindDirection? Unwind { get; set; }
    public bool HasArtwork { get; set; }

    /// <summary>Display-only: the uploaded artwork's file name, shown next to the download link.</summary>
    public string? ArtworkFileName { get; set; }

    /// <summary>Display-only: the production job already created for this line, when any.</summary>
    public string? JobNumber { get; set; }
    public bool HasJob { get; set; }

    public IFormFile? ArtworkFile { get; set; }

    /// <summary>The quoted description snapshot; editable on the line.</summary>
    public string? Description { get; set; }

    /// <summary>The line's ordered spec, round-tripped through the form as JSON. The spec editor
    /// fields below override individual values on save; JSON-only fields (artwork path, spots and
    /// finishing when the editor wasn't rendered) survive untouched.</summary>
    public string? SpecJson { get; set; }

    /// <summary>True when the spec editor was rendered for this line, so empty spot/finishing
    /// lists mean "cleared" rather than "not shown".</summary>
    public bool SpecEdited { get; set; }

    [Range(0.01, 100)] public decimal? LabelAcrossIn { get; set; }
    [Range(0.01, 100)] public decimal? LabelAroundIn { get; set; }
    [Range(0, 10)] public decimal? CornerRadiusIn { get; set; }
    [Range(0, 10)] public decimal? GutterAcrossIn { get; set; }
    [Range(0, 10)] public decimal? GutterAroundIn { get; set; }
    [Range(0, 10)] public decimal? BleedIn { get; set; }
    public Guid? SubstrateId { get; set; }
    public Guid? DieId { get; set; }
    [Range(0.0001, 1000)] public decimal? ShrinkLayflatIn { get; set; }
    public InkSet? InkSet { get; set; }
    [Range(0, 3)] public int? WhiteHits { get; set; }
    [Range(0, 100)] public decimal? WhiteCoveragePct { get; set; }
    [Range(0, 10000)] public decimal? SetupWasteImpressions { get; set; }
    [Range(0, 1)] public decimal? RunningWastePct { get; set; }
    [Range(1, 100)] public int? MaxLabelsAcrossOverride { get; set; }
    public LabelOrientation? LabelOrientationOverride { get; set; }
    public List<OrderSpotPageInput> Spots { get; set; } = [];
    public List<OrderFinishingPageInput> Finishing { get; set; } = [];

    public LabelSpec? DeserializeSpec() => string.IsNullOrWhiteSpace(SpecJson)
        ? null
        : System.Text.Json.JsonSerializer.Deserialize<LabelSpec>(SpecJson);

    /// <summary>The spec to persist: the round-tripped snapshot with any edited fields applied.</summary>
    public LabelSpec? BuildSpec()
    {
        var baseSpec = DeserializeSpec();
        if (baseSpec is null)
        {
            return null; // no snapshot yet — the service seeds one from the product
        }

        if (!SpecEdited)
        {
            return baseSpec with { Unwind = Unwind };
        }

        var spots = Spots
            .Where(s => s.InkId != Guid.Empty)
            .Select((s, index) => new Services.Estimates.SpotSelectionInput(s.InkId, s.Hits, s.CoveragePct / 100m, index))
            .ToList();
        var finishing = Finishing
            .Where(f => f.Include && f.OperationId != Guid.Empty)
            .Select((f, index) => new Services.Estimates.FinishingOperationSelectionInput(
                f.OperationId, f.SetupMinutesOverride, f.RunSpeedFpmOverride, index, f.StockId, f.DieId))
            .ToList();

        return baseSpec with
        {
            LabelAcrossIn = LabelAcrossIn ?? baseSpec.LabelAcrossIn,
            LabelAroundIn = LabelAroundIn ?? baseSpec.LabelAroundIn,
            CornerRadiusIn = CornerRadiusIn ?? baseSpec.CornerRadiusIn,
            GutterAcrossIn = GutterAcrossIn ?? baseSpec.GutterAcrossIn,
            GutterAroundIn = GutterAroundIn ?? baseSpec.GutterAroundIn,
            BleedIn = BleedIn ?? baseSpec.BleedIn,
            SubstrateId = SubstrateId is { } substrateId && substrateId != Guid.Empty ? substrateId : baseSpec.SubstrateId,
            DieId = DieId is { } dieId && dieId != Guid.Empty ? dieId : null,
            ShrinkLayflatIn = ShrinkLayflatIn,
            InkSet = InkSet ?? baseSpec.InkSet,
            WhiteHits = WhiteHits ?? baseSpec.WhiteHits,
            WhiteCoveragePct = WhiteCoveragePct.HasValue ? WhiteCoveragePct.Value / 100m : baseSpec.WhiteCoveragePct,
            SpotsJson = Services.Estimates.EstimateCalculationMapper.SerializeSpots(spots),
            FinishingOperationsJson = Services.Estimates.EstimateCalculationMapper.SerializeFinishingOperations(finishing),
            SetupWasteImpressions = SetupWasteImpressions ?? baseSpec.SetupWasteImpressions,
            RunningWastePct = RunningWastePct ?? baseSpec.RunningWastePct,
            MaxLabelsAcrossOverride = MaxLabelsAcrossOverride,
            LabelOrientationOverride = LabelOrientationOverride,
            Unwind = Unwind
        };
    }
}

[Authorize(Policy = TransactionPolicies.SalesOrdersEdit)]
public class CreateModel(
    SalesOrderService salesOrderService,
    ProductService productService,
    ArtworkService artworkService,
    ShippingMethodService shippingMethodService,
    CustomerService customerService,
    OutsourceService outsourceService,
    LabelsMisDbContext db) : PageModel
{
    [BindProperty] public SalesOrderPageInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadLookupsAsync(null, cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(Input.CustomerId, cancellationToken);
            return Page();
        }

        try
        {
            var order = await salesOrderService.CreateAsync(Input.ToForm(), cancellationToken);
            await UploadLineArtworkAsync(cancellationToken);
            return RedirectToPage("Edit", new { id = order.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadLookupsAsync(Input.CustomerId, cancellationToken);
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

    public async Task<IActionResult> OnGetProductsAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        var products = !customerId.HasValue || customerId.Value == Guid.Empty
            ? await productService.ListPickerAllAsync(cancellationToken)
            : await productService.ListPickerForCustomerAsync(customerId.Value, cancellationToken);
        return new JsonResult(products);
    }

    public async Task<IActionResult> OnGetSuggestedPriceAsync(Guid productId, int quantity, CancellationToken cancellationToken)
    {
        var price = await salesOrderService.GetSuggestedUnitPriceAsync(productId, quantity, cancellationToken);
        return new JsonResult(new { unitPrice = price });
    }

    public Task<IActionResult> OnGetAddressesAsync(Guid? customerId, CancellationToken cancellationToken) =>
        ShipToAddressJson.BuildAsync(customerService, customerId, cancellationToken);

    private async Task LoadLookupsAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        ViewData["ShowProductFilter"] = true;
        ViewData["OutsourceVendors"] = await outsourceService.GetVendorOptionsAsync(
            Input.Lines.Select(l => l.OutsourceVendorId).Concat(Input.Charges.Select(c => c.OutsourceVendorId)),
            cancellationToken);
        ViewData["ShippingMethods"] = await shippingMethodService.GetSelectableAsync(
            Input.ShippingMethodId, cancellationToken);
        ViewData["Customers"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
        if (customerId.HasValue)
        {
            ViewData["Products"] = await db.Products.AsNoTracking()
                .Where(p => p.IsActive && p.CustomerAssignments.Any(a => a.CustomerId == customerId))
                .OrderBy(p => p.InternalSku)
                .Select(p => new SelectListItem($"{p.InternalSku} — {p.Description}", p.Id.ToString()))
                .ToListAsync(cancellationToken);
        }
    }
}
