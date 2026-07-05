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

[Authorize(Policy = TransactionPolicies.EstimatesEdit)]
public class CreateModel(
    EstimateService estimateService,
    LabelsMisDbContext db,
    UserManager<ApplicationUser> userManager,
    ShippingMethodService shippingMethodService) : PageModel
{
    [BindProperty]
    public EstimatePageInput Input { get; set; } = new();

    [BindProperty]
    public bool SendEmail { get; set; } = true;

    [BindProperty]
    public string? EmailTo { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input.ValidUntilDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        await LoadLookupsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveDraftAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var estimate = await estimateService.CreateDraftAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = estimate.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPreviewPdfAsync(CancellationToken cancellationToken)
    {
        var bytes = await estimateService.GeneratePreviewAsync(Input.ToForm(), "DRAFT", 1, cancellationToken);
        return File(bytes, "application/pdf");
    }

    public async Task<IActionResult> OnPostSaveAndSendAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        Guid estimateId;
        try
        {
            var estimate = await estimateService.CreateDraftAsync(Input.ToForm(), cancellationToken);
            estimateId = estimate.Id;
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        // The draft now exists; send it and surface any email failure on the edit page.
        try
        {
            await estimateService.SendAsync(estimateId, SendEmail ? EmailTo : null, null, null, includePdf: true, cancellationToken);
            TempData["EstimateStatus"] = SendEmail && !string.IsNullOrWhiteSpace(EmailTo)
                ? $"Estimate created and emailed to {EmailTo}."
                : "Estimate created and marked sent.";
        }
        catch (Exception ex)
        {
            TempData["EstimateError"] = $"Estimate created and marked sent, but the email could not be delivered: {ex.Message}";
        }

        return RedirectToPage("Edit", new { id = estimateId });
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
