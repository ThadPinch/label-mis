using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Products;

[Authorize(Policy = TransactionPolicies.ProductsRead)]
public class EditModel(ProductService productService, ArtworkService artworkService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public ProductPageInput Input { get; set; } = new();
    [BindProperty] public IFormFile? ArtworkFile { get; set; }
    public Domain.Entities.Product? Product { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanAdmin { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Estimator);
        CanAdmin = User.IsInRole(AppRoles.Admin);
        Product = await productService.GetAsync(Id, cancellationToken);
        if (Product is null) return NotFound();
        Input = Map(Product);
        await LoadLookupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator)) return Forbid();
        EnsureCustomerSelection();
        ValidateRequiredSelections();
        if (!ModelState.IsValid)
        {
            Product = await productService.GetAsync(Id, cancellationToken);
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        try
        {
            await productService.UpdateAsync(Id, Input.ToForm(), cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Product = await productService.GetAsync(Id, cancellationToken);
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadArtworkAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator)) return Forbid();

        if (ArtworkFile is null || ArtworkFile.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Select an artwork file to upload.");
            return await ReloadPageAsync(cancellationToken);
        }

        try
        {
            await artworkService.UploadForProductAsync(Id, ArtworkFile, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadPageAsync(cancellationToken);
        }
    }

    private async Task<IActionResult> ReloadPageAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Estimator);
        CanAdmin = User.IsInRole(AppRoles.Admin);
        Product = await productService.GetAsync(Id, cancellationToken);
        if (Product is null) return NotFound();
        Input = Map(Product);
        await LoadLookupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDiscontinueAsync(CancellationToken cancellationToken)
    {
        var admin = User.IsInRole(AppRoles.Admin);
        if (!admin && !User.IsInRole(AppRoles.Estimator)) return Forbid();
        await productService.DiscontinueAsync(Id, admin, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    private static ProductPageInput Map(Domain.Entities.Product product)
    {
        var input = new ProductPageInput
        {
            PrimaryCustomerId = product.PrimaryCustomerId,
            CustomerIds = product.CustomerAssignments.Select(a => a.CustomerId).ToList(),
            CustomerSku = product.CustomerSku,
            Description = product.Description,
            SourceEstimateLineId = product.SourceEstimateLineId,
            LabelAcrossIn = product.LabelAcrossIn,
            LabelAroundIn = product.LabelAroundIn,
            CornerRadiusIn = product.CornerRadiusIn,
            SubstrateId = product.SubstrateId,
            InkSet = product.InkSet,
            FinishingOperations = Services.Estimates.EstimateCalculationMapper
                .DeserializeFinishingOperations(product.FinishingOperationsJson).ToList(),
            DieId = product.DieId,
            ArtworkFilePath = product.ArtworkFilePath
        };
        if (product.RollSpec is not null)
        {
            input.LabelsPerRoll = product.RollSpec.LabelsPerRoll;
            input.CoreSizeIn = product.RollSpec.CoreSizeIn;
            input.UnwindPosition = product.RollSpec.UnwindPosition;
            input.MaxOdIn = product.RollSpec.MaxOdIn;
            input.RollsPerCase = product.RollSpec.RollsPerCase;
            input.CaseLabelFormat = product.RollSpec.CaseLabelFormat;
        }
        return input;
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        ViewData["Customers"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Substrates"] = await db.Stocks.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code)
            .Select(s => new SelectListItem(s.Description, s.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Dies"] = await db.Dies.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Description)
            .Select(d => new SelectListItem(d.Description, d.Id.ToString())).ToListAsync(cancellationToken);
    }

    private void EnsureCustomerSelection()
    {
        if (Input.CustomerIds.Count == 0 && Input.PrimaryCustomerId != Guid.Empty)
        {
            Input.CustomerIds.Add(Input.PrimaryCustomerId);
        }
    }

    private void ValidateRequiredSelections()
    {
        if (Input.PrimaryCustomerId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(Input.PrimaryCustomerId), "Select a primary customer.");
        }

        if (Input.SubstrateId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(Input.SubstrateId), "Select a substrate.");
        }

        if (Input.CustomerIds.Count == 0)
        {
            ModelState.AddModelError(nameof(Input.CustomerIds), "Select at least one customer.");
        }
    }
}
