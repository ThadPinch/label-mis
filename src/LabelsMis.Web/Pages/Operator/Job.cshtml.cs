using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Operator;

public class JobModel(JobService jobService) : PageModel
{
    [BindProperty(SupportsGet = true)] public string JobNumber { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var view = await jobService.GetOperatorViewByNumberAsync(JobNumber, cancellationToken);
        if (view is null) return NotFound();
        return RedirectToPage("/Jobs/Detail", new { id = view.Job.Id });
    }
}
