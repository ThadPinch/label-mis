using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Setup;

[Authorize]
public class DocumentationModel : PageModel
{
    public void OnGet()
    {
    }
}
