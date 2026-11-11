using LabelsMis.Domain.QuickBooks;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.QuickBooks;

/// <summary>Where Intuit sends the browser after the consent screen. It checks that the answer
/// belongs to a connect this browser started, finishes the connection, and always redirects back
/// to Setup → QuickBooks with the outcome.</summary>
[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Accounting)]
public class CallbackModel(QuickBooksConnectionService connectionService, PublicUrlService publicUrl) : PageModel
{
    public const string StatusKey = "QuickBooksConnectionStatus";
    public const string ErrorKey = "QuickBooksConnectionError";

    public async Task<IActionResult> OnGetAsync(
        string? code, string? state, string? realmId, string? error, CancellationToken cancellationToken)
    {
        if (!QuickBooksOAuthState.Consume(HttpContext, state))
        {
            TempData[ErrorKey] =
                "QuickBooks was not connected: the answer didn't match a connect started in this browser. "
                + "Press Connect to QuickBooks again.";
            return BackToSetup();
        }

        if (!string.IsNullOrEmpty(error))
        {
            TempData[ErrorKey] = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? "QuickBooks was not connected: the request was declined on the Intuit screen."
                : "QuickBooks was not connected: Intuit reported a problem on its sign-in screen.";
            return BackToSetup();
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(realmId))
        {
            TempData[ErrorKey] = "QuickBooks was not connected: Intuit did not say which company to connect.";
            return BackToSetup();
        }

        try
        {
            var result = await connectionService.CompleteConnectAsync(
                code, realmId, publicUrl.Absolute(QuickBooksConnectionService.CallbackPath), cancellationToken);
            TempData[StatusKey] = result.Warning is null
                ? $"Connected to {result.CompanyName ?? "QuickBooks"}."
                : $"Connected to QuickBooks, but the company's details could not be read yet ({result.Warning}). Press Test connection to try again.";
        }
        catch (Exception ex) when (ex is QuickBooksAuthException or SecretProtectionException
                                       or InvalidOperationException or ArgumentException)
        {
            TempData[ErrorKey] = $"QuickBooks was not connected: {ex.Message}";
        }

        return BackToSetup();
    }

    private RedirectToPageResult BackToSetup() => RedirectToPage("/Settings/QuickBooks");
}
