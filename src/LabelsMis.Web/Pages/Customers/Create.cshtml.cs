using LabelsMis.Domain.Estimating;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Customers;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(
    CustomerService customerService,
    UserManager<ApplicationUser> userManager,
    GeneralSettingsService generalSettings) : PageModel
{
    [BindProperty]
    public CustomerFormInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["CanEditForm"] = true;
        // New customers start at the shop-wide default markup (Settings → General), shown as margin.
        Input.DefaultMarginPercent = PricingMath.MarkupToMarginPercent(await ShopDefaultMarkupAsync(cancellationToken));
        await LoadSalesRepOptionsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Input.NormalizeCollections();
        if (!ModelState.IsValid)
        {
            ViewData["CanEditForm"] = true;
            await LoadSalesRepOptionsAsync(cancellationToken);
            return Page();
        }

        try
        {
            // An untouched margin field stores the shop default markup exactly, not its rounded margin re-converted.
            var shopDefault = await ShopDefaultMarkupAsync(cancellationToken);
            var customer = await customerService.CreateAsync(Input.ToForm(shopDefault), cancellationToken);
            return RedirectToPage("Edit", new { id = customer.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewData["CanEditForm"] = true;
            await LoadSalesRepOptionsAsync(cancellationToken);
            return Page();
        }
    }

    private async Task<decimal> ShopDefaultMarkupAsync(CancellationToken cancellationToken)
    {
        var settings = await generalSettings.GetAsync(cancellationToken);
        return settings?.DefaultMarkupPct ?? Domain.Entities.GeneralSettings.DefaultMarkupPctFallback;
    }

    private async Task LoadSalesRepOptionsAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        ViewData["SalesRepOptions"] = users.Select(u => new SelectListItem(
            u.Email ?? u.UserName ?? u.Id.ToString(),
            u.Id.ToString())).ToList();
    }
}
