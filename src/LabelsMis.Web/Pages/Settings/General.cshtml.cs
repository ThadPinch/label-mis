using System.ComponentModel.DataAnnotations;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Settings;

[Authorize(Roles = AppRoles.Admin)]
public class GeneralModel(GeneralSettingsService generalSettings) : PageModel
{
    private static readonly string[] AllowedLogoTypes =
        ["image/png", "image/jpeg", "image/gif", "image/webp"];
    private const long MaxLogoBytes = 2 * 1024 * 1024; // 2 MB

    [BindProperty] public GeneralSettingsPageInput Input { get; set; } = new();

    [BindProperty]
    [Display(Name = "Logo")]
    public IFormFile? LogoUpload { get; set; }

    public string? LogoDataUri { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var settings = await generalSettings.GetOrCreateAsync(cancellationToken);
        Input = new GeneralSettingsPageInput
        {
            CompanyName = settings.CompanyName,
            AddressLine1 = settings.AddressLine1,
            AddressLine2 = settings.AddressLine2,
            City = settings.City,
            State = settings.State,
            Zip = settings.Zip,
            Phone = settings.Phone,
            Email = settings.Email,
            Website = settings.Website,
            TermsText = settings.TermsText
        };
        LogoDataUri = BuildLogoDataUri(settings.LogoBytes, settings.LogoContentType);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (LogoUpload is not null)
        {
            if (!AllowedLogoTypes.Contains(LogoUpload.ContentType))
            {
                ModelState.AddModelError(nameof(LogoUpload), "Logo must be a PNG, JPEG, GIF, or WebP image.");
            }
            else if (LogoUpload.Length > MaxLogoBytes)
            {
                ModelState.AddModelError(nameof(LogoUpload), "Logo must be 2 MB or smaller.");
            }
        }

        if (!ModelState.IsValid)
        {
            await ReloadLogoAsync(cancellationToken);
            return Page();
        }

        await generalSettings.UpdateAsync(new GeneralSettingsFormInput(
            Input.CompanyName,
            Input.AddressLine1,
            Input.AddressLine2,
            Input.City,
            Input.State,
            Input.Zip,
            Input.Phone,
            Input.Email,
            Input.Website,
            Input.TermsText), cancellationToken);

        if (LogoUpload is not null)
        {
            using var stream = new MemoryStream();
            await LogoUpload.CopyToAsync(stream, cancellationToken);
            await generalSettings.SetLogoAsync(stream.ToArray(), LogoUpload.ContentType, cancellationToken);
        }

        TempData["GeneralStatus"] = "General settings saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveLogoAsync(CancellationToken cancellationToken)
    {
        await generalSettings.ClearLogoAsync(cancellationToken);
        TempData["GeneralStatus"] = "Logo removed.";
        return RedirectToPage();
    }

    private async Task ReloadLogoAsync(CancellationToken cancellationToken)
    {
        var settings = await generalSettings.GetAsync(cancellationToken);
        LogoDataUri = BuildLogoDataUri(settings?.LogoBytes, settings?.LogoContentType);
    }

    private static string? BuildLogoDataUri(byte[]? bytes, string? contentType)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        var type = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
        return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
    }
}

public class GeneralSettingsPageInput
{
    [Required, StringLength(200)]
    [Display(Name = "Company name")]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Address line 1")]
    public string? AddressLine1 { get; set; }

    [StringLength(200)]
    [Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(100)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [StringLength(20)]
    [Display(Name = "ZIP")]
    public string? Zip { get; set; }

    [StringLength(50)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(200)]
    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(200)]
    [Display(Name = "Website")]
    public string? Website { get; set; }

    [StringLength(2000)]
    [Display(Name = "Estimate terms text")]
    public string? TermsText { get; set; }
}
