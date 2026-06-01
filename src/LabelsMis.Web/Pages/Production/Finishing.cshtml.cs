using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace LabelsMis.Web.Pages.Production;

public class FinishingModel(JobService jobService) : ProductionStageModel(jobService)
{
    public override JobStatus Stage => JobStatus.Printed;
    public override JobStatus? NextStatus => JobStatus.Finished;
    public override string StageTitle => "Finishing";
    public override string AdvanceLabel => "Mark finished";

    public IReadOnlyList<FinishingJobView> FinishingJobs { get; private set; } = [];

    public override async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadStageCountsAsync(cancellationToken);
        FinishingJobs = await JobService.ListFinishingJobsAsync(Search, cancellationToken);
    }

    public async Task<IActionResult> OnPostCompleteTaskAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!CanUserAdvance())
        {
            return Forbid();
        }

        await JobService.CompleteFinishingTaskAsync(operationId, cancellationToken);
        return RedirectToStage();
    }
}
