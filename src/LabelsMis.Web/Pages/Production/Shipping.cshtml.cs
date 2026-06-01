using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;

namespace LabelsMis.Web.Pages.Production;

public class ShippingModel(JobService jobService) : ProductionStageModel(jobService)
{
    public override JobStatus Stage => JobStatus.Rewound;
    public override JobStatus? NextStatus => JobStatus.Shipped;
    public override string StageTitle => "Shipping";
    public override string AdvanceLabel => "Mark shipped";
}
