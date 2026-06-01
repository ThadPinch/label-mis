using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;

namespace LabelsMis.Web.Pages.Production;

public class RewindingModel(JobService jobService) : ProductionStageModel(jobService)
{
    public override JobStatus Stage => JobStatus.Finished;
    public override JobStatus? NextStatus => JobStatus.Rewound;
    public override string StageTitle => "Rewinding";
    public override string AdvanceLabel => "Mark rewound";
}
