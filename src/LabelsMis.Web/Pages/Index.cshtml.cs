using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LabelsMis.Infrastructure.Identity;

namespace LabelsMis.Web.Pages;

[Authorize]
public class IndexModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public string DisplayName { get; private set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        DisplayName = user?.UserName ?? User.Identity?.Name ?? "User";

        if (user is not null)
        {
            Roles = (await userManager.GetRolesAsync(user)).OrderBy(r => r).ToList();
        }
    }
}
