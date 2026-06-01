using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;

namespace LabelsMis.Web.Pages.Production;

public class PressModel(JobService jobService) : ProductionStageModel(jobService)
{
    public override JobStatus Stage => JobStatus.Queued;
    public override JobStatus? NextStatus => JobStatus.Printed;
    public override string StageTitle => "Press";
    public override string AdvanceLabel => "Mark printed";
}
