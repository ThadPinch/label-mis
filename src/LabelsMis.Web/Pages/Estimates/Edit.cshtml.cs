using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Estimates;

[Authorize(Policy = TransactionPolicies.EstimatesRead)]
public class EditModel(
    EstimateService estimateService,
    LabelsMisDbContext db,
    UserManager<ApplicationUser> userManager,
    ShippingMethodService shippingMethodService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public EstimatePageInput Input { get; set; } = new();

    [BindProperty]
    public string? LostReason { get; set; }

    [BindProperty]
    public string? EmailTo { get; set; }

    [BindProperty]
    public string? EmailSubject { get; set; }

    [BindProperty]
    public string? EmailBody { get; set; }

    [BindProperty]
    public bool IncludePdf { get; set; }

    public EstimateDetail? Detail { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanAdmin { get; private set; }
    public bool CanDelete { get; private set; }
    public bool IsReadOnly => Detail?.Estimate.Status is not EstimateStatus.Draft;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Estimator);
        CanAdmin = User.IsInRole(AppRoles.Admin);
        Detail = await estimateService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null)
        {
            return NotFound();
        }

        CanDelete = CanEdit
            && Detail.Estimate.Status == EstimateStatus.Draft
            && Detail.SalesOrderId is null;

        Input = EstimatePageInput.FromEstimate(Detail.Estimate);

        // Pre-fill the email composer: recipient (primary contact first),
        // a default subject/body, and the "include PDF" attachment on.
        EmailTo = Detail.Customer.Contacts
            .OrderByDescending(c => c.IsPrimary)
            .Select(c => c.Email)
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        EmailSubject = $"Estimate {Detail.Estimate.EstimateNumber}";
        EmailBody = $"Please find attached estimate {Detail.Estimate.EstimateNumber}.";
        IncludePdf = true;

        await LoadLookupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveDraftAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await ReloadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await estimateService.UpdateDraftAsync(Id, Input.ToForm(), cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSendAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator))
        {
            return Forbid();
        }

        if (ModelState.IsValid)
        {
            await estimateService.UpdateDraftAsync(Id, Input.ToForm(), cancellationToken);
        }

        try
        {
            await estimateService.SendAsync(Id, EmailTo, EmailSubject, EmailBody, IncludePdf, cancellationToken);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Estimate marked sent, but the email could not be delivered: {ex.Message}");
            await ReloadAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostPreviewPdfAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator))
        {
            return Forbid();
        }

        var info = await db.Estimates.AsNoTracking()
            .Where(e => e.Id == Id)
            .Select(e => new { e.EstimateNumber, e.RevisionNumber })
            .FirstOrDefaultAsync(cancellationToken);

        var bytes = await estimateService.GeneratePreviewAsync(
            Input.ToForm(), info?.EstimateNumber, info?.RevisionNumber ?? 1, cancellationToken);
        return File(bytes, "application/pdf");
    }

    public async Task<IActionResult> OnGetPdfAsync(CancellationToken cancellationToken)
    {
        var bytes = await estimateService.RenderPdfBytesAsync(Id, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator))
        {
            return Forbid();
        }

        try
        {
            await estimateService.ResendAsync(Id, EmailTo, EmailSubject, EmailBody, IncludePdf, cancellationToken);
            TempData["EstimateStatus"] = $"Estimate emailed to {EmailTo}.";
        }
        catch (Exception ex)
        {
            TempData["EstimateError"] = $"Could not send the estimate email: {ex.Message}";
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostMarkWonAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            return Forbid();
        }

        await estimateService.MarkWonAsync(Id, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostMarkLostAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            return Forbid();
        }

        await estimateService.MarkLostAsync(Id, LostReason, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostCreateRevisionAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator))
        {
            return Forbid();
        }

        await estimateService.CreateRevisionAsync(Id, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Estimator))
        {
            return Forbid();
        }

        try
        {
            await estimateService.DeleteAsync(Id, cancellationToken);
            return RedirectToPage("./Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Estimator);
        CanAdmin = User.IsInRole(AppRoles.Admin);
        Detail = await estimateService.GetDetailAsync(Id, cancellationToken);
        CanDelete = CanEdit
            && Detail?.Estimate.Status == EstimateStatus.Draft
            && Detail.SalesOrderId is null;
        await LoadLookupsAsync(cancellationToken);
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        ViewData["Customers"] = await db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync(cancellationToken);

        ViewData["Substrates"] = await db.Stocks.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Code)
            .Select(s => new SelectListItem($"{s.Code} — {s.WidthIn}\" @ {s.CostPerMsi:C4}/MSI", s.Id.ToString()))
            .ToListAsync(cancellationToken);

        ViewData["FinishingOperations"] = await db.FinishingOperations.AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.Code)
            .ToListAsync(cancellationToken);

        ViewData["SpotInks"] = await db.Inks.AsNoTracking()
            .Where(i => i.IsActive && i.IsSpot && i.SpotColor != SpotColor.White)
            .OrderBy(i => i.Code)
            .ToListAsync(cancellationToken);

        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        ViewData["SalesRepOptions"] = users.Select(u => new SelectListItem(
            u.Email ?? u.UserName ?? u.Id.ToString(), u.Id.ToString())).ToList();

        ViewData["ShippingMethods"] = await shippingMethodService.GetSelectableAsync(
            Input.ShippingMethodId, cancellationToken);
    }
}
