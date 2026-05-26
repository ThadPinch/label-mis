using System.ComponentModel.DataAnnotations;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Settings;

[Authorize(Roles = AppRoles.Admin)]
public class StorageModel(StorageSettingsService storageSettings) : PageModel
{
    [BindProperty] public StorageSettingsPageInput Input { get; set; } = new();
    public bool HasSecretKey { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var settings = await storageSettings.GetOrCreateAsync(cancellationToken);
        HasSecretKey = !string.IsNullOrWhiteSpace(settings.SecretKey);
        Input = new StorageSettingsPageInput
        {
            ServiceUrl = settings.ServiceUrl,
            BucketName = settings.BucketName,
            AccessKey = settings.AccessKey,
            Region = settings.Region,
            ArtworkKeyPrefix = settings.ArtworkKeyPrefix,
            UseLocalFallback = settings.UseLocalFallback
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var existing = await storageSettings.GetOrCreateAsync(cancellationToken);
            HasSecretKey = !string.IsNullOrWhiteSpace(existing.SecretKey);
            return Page();
        }

        var current = await storageSettings.GetOrCreateAsync(cancellationToken);
        var secret = string.IsNullOrWhiteSpace(Input.SecretKey) ? current.SecretKey : Input.SecretKey;
        await storageSettings.UpdateAsync(new StorageSettingsFormInput(
            Input.ServiceUrl,
            Input.BucketName,
            Input.AccessKey,
            secret,
            Input.Region,
            Input.ArtworkKeyPrefix,
            Input.UseLocalFallback), cancellationToken);

        return RedirectToPage();
    }
}

public class StorageSettingsPageInput
{
    [StringLength(500)] public string ServiceUrl { get; set; } = string.Empty;
    [StringLength(200)] public string BucketName { get; set; } = string.Empty;
    [StringLength(200)] public string AccessKey { get; set; } = string.Empty;
    [StringLength(500)] public string? SecretKey { get; set; }
    [StringLength(100)] public string? Region { get; set; }
    [Required, StringLength(200)] public string ArtworkKeyPrefix { get; set; } = "artwork/";
    public bool UseLocalFallback { get; set; } = true;
}
