using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Users;

[Authorize(Policy = TransactionPolicies.AdminOverride)]
public class IndexModel(UserAdminService userAdminService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    public IReadOnlyList<UserListItem> Users { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Users = await userAdminService.ListAsync(Search, Sort, cancellationToken);
}
