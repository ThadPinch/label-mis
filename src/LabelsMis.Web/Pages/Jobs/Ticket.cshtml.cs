using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Jobs;

[Authorize(Policy = TransactionPolicies.JobsRead)]
public class TicketModel(JobService jobService, JobTicketPdfGenerator pdfGenerator) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var detail = await jobService.GetTicketDetailAsync(Id, cancellationToken);
        if (detail is null) return NotFound();

        var orderJobs = await jobService.GetOrderTicketDetailsAsync(Id, cancellationToken);
        var pdf = await pdfGenerator.GenerateAsync(detail, orderJobs, cancellationToken);
        return File(pdf, "application/pdf", $"{detail.Job.JobNumber.Replace('/', '-')}-ticket.pdf");
    }
}
