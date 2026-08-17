using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.Outsourcing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Production;

/// <summary>
/// The Outsourced stage: every order line and additional charge that an outside vendor is making
/// for us — sent-to-vendor / expected-in tracking and receiving (partials welcome). A received order
/// line's job moves straight to ready-to-ship; a received charge simply closes out.
/// </summary>
[Authorize(Policy = TransactionPolicies.JobsRead)]
public class OutsourcedModel(OutsourceService outsourceService, JobService jobService) : PageModel, IProductionStageNav
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? VendorId { get; set; }
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "open";
    [BindProperty(SupportsGet = true)] public bool OverdueOnly { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    public PagedResult<OutsourcedItemRow> Result { get; private set; } = null!;
    public List<SelectListItem> VendorOptions { get; private set; } = [];
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    public JobStatus Stage => JobStatus.Outsourced;
    public IReadOnlyDictionary<JobStatus, int> StageCounts { get; private set; } = new Dictionary<JobStatus, int>();

    public bool CanAct => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Operator);

    /// <summary>Filter values every sort/pager link carries.</summary>
    public IReadOnlyDictionary<string, string?> FilterRoute => new Dictionary<string, string?>
    {
        ["Search"] = Search,
        ["VendorId"] = VendorId?.ToString(),
        ["Status"] = Status == "open" ? null : Status,
        ["OverdueOnly"] = OverdueOnly ? "true" : null
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        StageCounts = await jobService.GetStatusCountsAsync(ProductionStages.All.Select(s => s.Status), cancellationToken);
        VendorOptions = await outsourceService.GetVendorOptionsAsync([VendorId], cancellationToken);
        Result = await outsourceService.ListAsync(Search, VendorId, Status, OverdueOnly, Sort, PageNumber, 50, cancellationToken);
    }

    /// <summary>Serves the send/receive popup body, fetched into the shared modal shell.</summary>
    public async Task<IActionResult> OnGetActionPanelAsync(Guid itemId, CancellationToken cancellationToken)
    {
        if (!CanAct)
        {
            return Forbid();
        }

        var panel = await outsourceService.GetActionPanelAsync(itemId, cancellationToken);
        return panel is null ? NotFound() : Partial("_OutsourceActionPanel", panel);
    }

    public async Task<IActionResult> OnPostMarkSentAsync(Guid itemId, DateOnly? sentOn, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!CanAct)
        {
            return Forbid();
        }

        try
        {
            await outsourceService.MarkSentAsync(itemId, sentOn, cancellationToken);
        }
        catch (Exception ex)
        {
            TempData["JobActionError"] = ex.Message;
        }

        return RedirectToReturnUrl(returnUrl);
    }

    public async Task<IActionResult> OnPostReceiveAsync(
        Guid itemId,
        int quantity,
        DateOnly? receivedOn,
        string? notes,
        bool markComplete,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!CanAct)
        {
            return Forbid();
        }

        try
        {
            await outsourceService.ReceiveAsync(itemId, quantity, receivedOn ?? Today, notes, markComplete, cancellationToken);
        }
        catch (Exception ex)
        {
            TempData["JobActionError"] = ex.Message;
        }

        return RedirectToReturnUrl(returnUrl);
    }

    public async Task<IActionResult> OnPostMarkCompleteAsync(Guid itemId, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!CanAct)
        {
            return Forbid();
        }

        try
        {
            await outsourceService.MarkCompleteAsync(itemId, cancellationToken);
        }
        catch (Exception ex)
        {
            TempData["JobActionError"] = ex.Message;
        }

        return RedirectToReturnUrl(returnUrl);
    }

    /// <summary>Back to the list the popup was opened from, so filters/sort/page survive.</summary>
    private IActionResult RedirectToReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToPage();
}
