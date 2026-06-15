using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Settings;

[Authorize(Roles = AppRoles.Admin)]
public class EmailModel(EmailSettingsService emailSettings) : PageModel
{
    [BindProperty] public EmailSettingsPageInput Input { get; set; } = new();
    public bool HasApiKey { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var settings = await emailSettings.GetOrCreateAsync(cancellationToken);
        HasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        Input = new EmailSettingsPageInput
        {
            Enabled = settings.Enabled,
            ApiBaseUrl = settings.ApiBaseUrl,
            Domain = settings.Domain,
            FromName = settings.FromName,
            FromEmail = settings.FromEmail
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var existing = await emailSettings.GetOrCreateAsync(cancellationToken);
            HasApiKey = !string.IsNullOrWhiteSpace(existing.ApiKey);
            return Page();
        }

        var current = await emailSettings.GetOrCreateAsync(cancellationToken);
        var apiKey = string.IsNullOrWhiteSpace(Input.ApiKey) ? current.ApiKey : Input.ApiKey;
        await emailSettings.UpdateAsync(new EmailSettingsFormInput(
            Input.Enabled,
            Input.ApiBaseUrl,
            Input.Domain,
            apiKey,
            Input.FromName,
            Input.FromEmail), cancellationToken);

        return RedirectToPage();
    }
}

public class EmailSettingsPageInput
{
    [Display(Name = "Enable Mailgun email")]
    public bool Enabled { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "API base URL")]
    public string ApiBaseUrl { get; set; } = EmailSettings.DefaultApiBaseUrl;

    [StringLength(200)]
    [Display(Name = "Sending domain")]
    public string Domain { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "API key")]
    public string? ApiKey { get; set; }

    [StringLength(200)]
    [Display(Name = "From name")]
    public string FromName { get; set; } = string.Empty;

    [StringLength(200)]
    [EmailAddress]
    [Display(Name = "From email")]
    public string FromEmail { get; set; } = string.Empty;
}
