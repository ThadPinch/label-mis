using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services.Rolls;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Rolls;

[Authorize(Policy = TransactionPolicies.InventoryRead)]
public class DetailModel(RollService rollService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public string? StageLocation { get; set; }
    [BindProperty] public string? ScrapNotes { get; set; }
    [BindProperty] public decimal SplitWidth1 { get; set; }
    [BindProperty] public decimal SplitWidth2 { get; set; }
    [BindProperty] public string? SplitLocation { get; set; }

    public RollDetail? Detail { get; private set; }
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Scheduler");

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Detail = await rollService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();
        if (Detail.Roll.WidthIn > 0)
        {
            SplitWidth1 = Detail.Roll.WidthIn / 2m;
            SplitWidth2 = Detail.Roll.WidthIn / 2m;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostStageAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        if (string.IsNullOrWhiteSpace(StageLocation)) { ModelState.AddModelError(nameof(StageLocation), "Location required."); return await ReloadAsync(cancellationToken); }
        await rollService.StageAsync(Id, StageLocation, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostScrapAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        await rollService.ScrapAsync(Id, ScrapNotes, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostSplitAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        try
        {
            await rollService.SplitAsync(Id, new RollSplitInput([SplitWidth1, SplitWidth2], SplitLocation), cancellationToken);
            return this.RedirectToListPage();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    private async Task<IActionResult> ReloadAsync(CancellationToken cancellationToken)
    {
        Detail = await rollService.GetDetailAsync(Id, cancellationToken);
        return Page();
    }
}
