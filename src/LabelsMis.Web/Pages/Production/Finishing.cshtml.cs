using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;

namespace LabelsMis.Web.Pages.Production;

public class FinishingModel(JobService jobService) : ProductionStageModel(jobService)
{
    public override JobStatus Stage => JobStatus.Printed;
    public override JobStatus? NextStatus => JobStatus.Finished;
    public override string StageTitle => "Finishing";

    // The popup lists the job's finishing tasks (each with time + laminate claim); the job
    // advances itself when the last task completes.
    public override string AdvanceLabel => "Complete tasks";
}
