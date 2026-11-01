using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.QuickBooks;
using LabelsMis.Web.Services.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Settings;

/// <summary>Setup → QuickBooks mapping: QB item per line type, class names and the class rule,
/// and the keywords that classify charge lines. Accounting keeps it, so Admin and Accounting.</summary>
[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Accounting)]
public class QuickBooksModel(QuickBooksMappingService mappingService, GeneralSettingsService generalSettings) : PageModel
{
    [BindProperty] public QuickBooksMappingInput Input { get; set; } = new();

    /// <summary>The E3 shipping settings, shown read-only with a link (Admin edits them).</summary>
    public decimal LocalDeliveryFee { get; private set; }
    public decimal CarrierCostMultiplier { get; private set; }
    public bool CanEditGeneral => User.IsInRole(AppRoles.Admin);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input = QuickBooksMappingInput.From(await mappingService.GetMappingAsync(cancellationToken));
        await LoadShippingAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadShippingAsync(cancellationToken);
            return Page();
        }

        try
        {
            await mappingService.UpdateAsync(Input.ToMapping(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadShippingAsync(cancellationToken);
            return Page();
        }

        TempData["QuickBooksStatus"] = "QuickBooks mapping saved.";
        return RedirectToPage();
    }

    private async Task LoadShippingAsync(CancellationToken cancellationToken)
    {
        var settings = await generalSettings.GetAsync(cancellationToken);
        LocalDeliveryFee = settings?.LocalDeliveryFee ?? Domain.Shipping.ShippingBillingSettings.Default.LocalDeliveryFee;
        CarrierCostMultiplier = settings?.CarrierCostMultiplier ?? Domain.Shipping.ShippingBillingSettings.Default.CarrierCostMultiplier;
    }
}

public class QuickBooksMappingInput
{
    [Required, StringLength(100)] [Display(Name = "Sales (product lines)")] public string SalesItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Shipping")] public string ShippingItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Design")] public string DesignItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Rush")] public string RushItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Die creation")] public string DieCreationItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Roll label")] public string RollLabelClass { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Shrink sleeves")] public string ShrinkSleevesClass { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Buyout")] public string BuyoutClass { get; set; } = string.Empty;
    [StringLength(500)] [Display(Name = "Design keywords")] public string? DesignKeywords { get; set; }
    [StringLength(500)] [Display(Name = "Rush keywords")] public string? RushKeywords { get; set; }
    [StringLength(500)] [Display(Name = "Die creation keywords")] public string? DieCreationKeywords { get; set; }
    [StringLength(50)] [Display(Name = "Buyout substrate code")] public string? OutsourcedSubstrateCode { get; set; }

    public static QuickBooksMappingInput From(QuickBooksMapping m) => new()
    {
        SalesItem = m.SalesItem,
        ShippingItem = m.ShippingItem,
        DesignItem = m.DesignItem,
        RushItem = m.RushItem,
        DieCreationItem = m.DieCreationItem,
        RollLabelClass = m.RollLabelClass,
        ShrinkSleevesClass = m.ShrinkSleevesClass,
        BuyoutClass = m.BuyoutClass,
        DesignKeywords = m.DesignKeywords,
        RushKeywords = m.RushKeywords,
        DieCreationKeywords = m.DieCreationKeywords,
        OutsourcedSubstrateCode = m.OutsourcedSubstrateCode
    };

    public QuickBooksMapping ToMapping() => new(
        SalesItem,
        ShippingItem,
        DesignItem,
        RushItem,
        DieCreationItem,
        RollLabelClass,
        ShrinkSleevesClass,
        BuyoutClass,
        DesignKeywords ?? string.Empty,
        RushKeywords ?? string.Empty,
        DieCreationKeywords ?? string.Empty,
        OutsourcedSubstrateCode ?? string.Empty);
}
