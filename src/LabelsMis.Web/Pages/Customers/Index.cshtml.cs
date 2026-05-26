using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Customers;

[Authorize(Policy = MasterDataPolicies.Read)]
public class IndexModel(CustomerService customerService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public bool IncludeInactive { get; set; }

    public PagedResult<CustomerListItem> Result { get; private set; } = null!;
    public bool CanEdit => currentUser.CanEditMasterData;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Search"] = Search;
        ViewData["Sort"] = Sort;
        ViewData["IncludeInactive"] = IncludeInactive;
        Result = await customerService.ListAsync(Search, Sort, PageNumber, 20, IncludeInactive, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit)
        {
            return Forbid();
        }

        await customerService.DeactivateAsync(id, cancellationToken);
        return RedirectToPage(new { Search, Sort, PageNumber, IncludeInactive });
    }
}
