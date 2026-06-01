using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;

namespace LabelsMis.Web.Pages.Production;

public class PrePressModel(JobService jobService) : ProductionStageModel(jobService)
{
    public override JobStatus Stage => JobStatus.PrePress;
    public override JobStatus? NextStatus => JobStatus.Queued;
    public override string StageTitle => "Pre-press";
    public override string AdvanceLabel => "Send to press";
}
