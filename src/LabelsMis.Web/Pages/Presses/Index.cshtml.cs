using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.Presses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Presses;

[Authorize(Policy = MasterDataPolicies.Read)]
public class IndexModel(PressService pressService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public bool IncludeInactive { get; set; }

    public PagedResult<PressListItem> Result { get; private set; } = null!;
    public bool CanEdit => currentUser.CanEditMasterData;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Search"] = Search;
        ViewData["IncludeInactive"] = IncludeInactive;
        Result = await pressService.ListAsync(Search, Sort, PageNumber, 20, IncludeInactive, cancellationToken);
    }
}
