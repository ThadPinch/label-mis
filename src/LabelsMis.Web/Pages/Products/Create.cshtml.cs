using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Products;

public class ProductPageInput
{
    public Guid? PrimaryCustomerId { get; set; }
    public List<Guid>? CustomerIds { get; set; } = [];
    public string? CustomerSku { get; set; }
    [Required, StringLength(500)] public string Description { get; set; } = string.Empty;
    public Guid? SourceEstimateLineId { get; set; }
    [Range(0.0001, 100)] public decimal LabelAcrossIn { get; set; } = 4m;
    [Range(0.0001, 100)] public decimal LabelAroundIn { get; set; } = 3m;
    [Range(0, 10)] public decimal CornerRadiusIn { get; set; }
    [Required] public Guid SubstrateId { get; set; }
    public InkSet InkSet { get; set; } = InkSet.CMYK;
    public List<FinishingOperationSelectionInput> FinishingOperations { get; set; } = [];
    public Guid? DieId { get; set; }
    public string? ArtworkFilePath { get; set; }
    public int LabelsPerRoll { get; set; }
    public decimal CoreSizeIn { get; set; } = 3m;
    [Range(1, 8)] public int UnwindPosition { get; set; } = 1;
    public decimal MaxOdIn { get; set; } = 8m;
    public int RollsPerCase { get; set; } = 1;
    public string? CaseLabelFormat { get; set; }

    public ProductFormInput ToForm() => new(
        PrimaryCustomerId,
        CustomerIds ?? [],
        CustomerSku,
        Description,
        SourceEstimateLineId,
        LabelAcrossIn,
        LabelAroundIn,
        CornerRadiusIn,
        SubstrateId,
        InkSet,
        FinishingOperations,
        DieId,
        ArtworkFilePath,
        LabelsPerRoll > 0
            ? new RollSpecInput(LabelsPerRoll, CoreSizeIn, UnwindPosition, MaxOdIn, RollsPerCase, CaseLabelFormat)
            : null);
}

[Authorize(Policy = TransactionPolicies.ProductsEdit)]
public class CreateModel(ProductService productService, LabelsMisDbContext db) : PageModel
{
    [BindProperty] public ProductPageInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadLookupsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        EnsureCustomerSelection();
        ValidateRequiredSelections();
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var product = await productService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = product.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        ViewData["Customers"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Substrates"] = await db.Stocks.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code)
            .Select(s => new SelectListItem(s.Description, s.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Dies"] = await db.Dies.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Description)
            .Select(d => new SelectListItem(d.Description, d.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["FinishingOperations"] = await db.FinishingOperations.AsNoTracking()
            .Where(o => o.IsActive).OrderBy(o => o.Code).ToListAsync(cancellationToken);
    }

    private void EnsureCustomerSelection()
    {
        // Products may have no customer. When one is chosen, keep primary and
        // assignments consistent: include the primary, or promote the first
        // assigned customer to primary when none is set.
        Input.CustomerIds ??= [];
        if (Input.PrimaryCustomerId is { } primary && primary != Guid.Empty)
        {
            if (!Input.CustomerIds.Contains(primary))
            {
                Input.CustomerIds.Add(primary);
            }
        }
        else if (Input.CustomerIds.Count > 0)
        {
            Input.PrimaryCustomerId = Input.CustomerIds[0];
        }
    }

    private void ValidateRequiredSelections()
    {
        if (Input.SubstrateId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(Input.SubstrateId), "Select a substrate.");
        }
    }
}
