using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Dies;
using LabelsMis.Web.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Dies;

[Authorize(Policy = MasterDataPolicies.Read)]
public class IndexModel(DieService dieService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public bool IncludeInactive { get; set; }

    public PagedResult<DieListItem> Result { get; private set; } = null!;
    public bool CanEdit => currentUser.CanEditMasterData;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Search"] = Search;
        ViewData["IncludeInactive"] = IncludeInactive;
        Result = await dieService.ListAsync(Search, sort: null, PageNumber, 20, IncludeInactive, cancellationToken);
    }
}
