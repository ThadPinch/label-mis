using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Products;

[Authorize(Policy = TransactionPolicies.ProductsRead)]
public class IndexModel(ProductService productService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CustomerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProductStatus? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<ProductListItem> Result { get; private set; } = null!;
    public bool CanEdit { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Estimator);
        Result = await productService.ListAsync(Search, CustomerId, Status, null, PageNumber, 25, false, cancellationToken);
        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }
}
