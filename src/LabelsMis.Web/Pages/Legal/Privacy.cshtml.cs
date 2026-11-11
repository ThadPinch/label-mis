using LabelsMis.Web.Services.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Legal;

/// <summary>The privacy policy, open without a sign-in: Intuit asks for a public address for it
/// before it issues production keys for the QuickBooks connection.</summary>
[AllowAnonymous]
public class PrivacyModel(GeneralSettingsService generalSettings) : PageModel
{
    public SiteOperator Operator { get; private set; } = SiteOperator.Unnamed;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Operator = SiteOperator.From(await generalSettings.GetAsync(cancellationToken));
}
