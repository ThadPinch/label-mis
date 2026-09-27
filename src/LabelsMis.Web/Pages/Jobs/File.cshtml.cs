using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Jobs;

/// <summary>Serves one of the job's production files — inline for previews, or as a download
/// under its original name.</summary>
[Authorize(Policy = TransactionPolicies.JobsRead)]
public class FileModel(JobFileService jobFiles) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, Guid fileId, bool inline, CancellationToken cancellationToken)
    {
        var file = await jobFiles.OpenAsync(id, fileId, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        return inline
            ? File(file.Value.Stream, file.Value.ContentType)
            : File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
    }
}
