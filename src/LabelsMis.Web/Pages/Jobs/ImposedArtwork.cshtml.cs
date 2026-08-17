using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Jobs;

/// <summary>Serves the job's imposed (step-and-repeat) PDF — inline for the job-page preview,
/// or as a download named after the job.</summary>
[Authorize(Policy = TransactionPolicies.JobsRead)]
public class ImposedArtworkModel(JobImpositionService impositionService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, bool inline, CancellationToken cancellationToken)
    {
        var file = await impositionService.OpenImposedAsync(id, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        return inline
            ? File(file.Value.Stream, "application/pdf")
            : File(file.Value.Stream, "application/pdf", file.Value.FileName);
    }
}
