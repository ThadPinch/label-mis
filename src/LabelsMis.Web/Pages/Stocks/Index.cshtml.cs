using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.Stocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Stocks;

[Authorize(Policy = MasterDataPolicies.Read)]
public class IndexModel(StockService stockService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public bool IncludeInactive { get; set; }

    [BindProperty(SupportsGet = true)]
    public LabelsMis.Domain.Enums.StockType? StockType { get; set; }

    public PagedResult<StockListItem> Result { get; private set; } = null!;
    public bool CanEdit => currentUser.CanEditMasterData;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Search"] = Search;
        ViewData["Sort"] = Sort;
        ViewData["IncludeInactive"] = IncludeInactive;
        Result = await stockService.ListAsync(Search, Sort, PageNumber, 20, IncludeInactive, StockType, cancellationToken);
    }
}
