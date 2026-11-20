using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Pages.Jobs;

[Authorize(Policy = TransactionPolicies.JobsRead)]
public class TicketModel(
    JobService jobService,
    JobTicketPdfGenerator pdfGenerator,
    JobTicketV2PdfGenerator pdfGeneratorV2,
    IOptions<JobOptions> jobOptions) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    /// <summary><c>?v=1</c> or <c>?v=2</c> picks the layout; without it the configured default prints.</summary>
    public async Task<IActionResult> OnGetAsync(int? v, CancellationToken cancellationToken)
    {
        var detail = await jobService.GetTicketDetailAsync(Id, cancellationToken);
        if (detail is null) return NotFound();

        var orderJobs = await jobService.GetOrderTicketDetailsAsync(Id, cancellationToken);
        var version = jobOptions.Value.ResolveTicketVersion(v);
        var pdf = version == JobOptions.TrialTicketVersion
            ? await pdfGeneratorV2.GenerateAsync(detail, orderJobs, cancellationToken)
            : await pdfGenerator.GenerateAsync(detail, orderJobs, cancellationToken);

        // The trial layout says so in its file name, so two downloads of one job can be told apart.
        var suffix = version == JobOptions.TrialTicketVersion ? "-ticket-v2.pdf" : "-ticket.pdf";
        return File(pdf, "application/pdf", detail.Job.JobNumber.Replace('/', '-') + suffix);
    }
}
