using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Mvc;

namespace LabelsMis.Web.ViewComponents;

/// <summary>The banner on the invoice pages when QuickBooks has stopped accepting the saved
/// sign-in. Renders nothing in every other state, including "never connected".</summary>
public class QuickBooksAlertViewComponent(QuickBooksConnectionService connectionService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        bool needsReconnect;
        try
        {
            needsReconnect = await connectionService.NeedsReconnectAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // A banner must never be the reason an invoice page fails to load.
            needsReconnect = false;
        }

        return needsReconnect ? View() : Content(string.Empty);
    }
}
