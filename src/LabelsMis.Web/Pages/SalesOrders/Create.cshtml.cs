using System.ComponentModel.DataAnnotations;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Products;
using LabelsMis.Web.Services.SalesOrders;
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
    public List<SalesOrderLinePageInput> Lines { get; set; } = [new()];

    public SalesOrderFormInput ToForm() => new(
        CustomerId, CustomerPoNumber, RequestedShipDate, Notes,
        Lines.Select(l => new SalesOrderLineInput(null, l.ProductId, null, l.Quantity, l.UnitPrice, l.LineNotes)).ToList());
}

public class SalesOrderLinePageInput
{
    public Guid ProductId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; } = 1000;
    [Range(0, double.MaxValue)] public decimal UnitPrice { get; set; }
    public string? LineNotes { get; set; }
    public bool HasArtwork { get; set; }
    public IFormFile? ArtworkFile { get; set; }
}

[Authorize(Policy = TransactionPolicies.SalesOrdersEdit)]
public class CreateModel(
    SalesOrderService salesOrderService,
    ProductService productService,
    ArtworkService artworkService,
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
        var order = await salesOrderService.CreateAsync(Input.ToForm(), cancellationToken);
        await UploadLineArtworkAsync(cancellationToken);
        return RedirectToPage("Edit", new { id = order.Id });
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

    private async Task LoadLookupsAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        ViewData["ShowProductFilter"] = true;
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
