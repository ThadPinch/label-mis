using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.QuickBooks;

/// <summary>Where QuickBooks sends someone who has disconnected LabelShop from the Apps menu inside
/// QuickBooks. It only explains: anyone can open this address, so it changes nothing. LabelShop
/// finds out about the disconnection itself, the next time QuickBooks refuses its sign-in.</summary>
[AllowAnonymous]
public class DisconnectedModel : PageModel
{
    public void OnGet()
    {
    }
}
