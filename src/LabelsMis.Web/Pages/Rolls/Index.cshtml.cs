using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Rolls;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Rolls;

[Authorize(Policy = TransactionPolicies.InventoryRead)]
public class IndexModel(RollService rollService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? StockId { get; set; }
    [BindProperty(SupportsGet = true)] public RollStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public StockType? StockType { get; set; }
    [BindProperty(SupportsGet = true)] public string? Location { get; set; }
    [BindProperty(SupportsGet = true)] public string? LotNumber { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<RollListItem> Result { get; private set; } = null!;
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Scheduler");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await rollService.ListAsync(Search, StockId, Status, Location, LotNumber, StockType, null, PageNumber, 25, cancellationToken);
        ViewData["StockOptions"] = await db.Stocks.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code)
            .Select(s => new SelectListItem($"{s.Code} — {s.Description}", s.Id.ToString())).ToListAsync(cancellationToken);
    }
}
