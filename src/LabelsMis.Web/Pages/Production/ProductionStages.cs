using LabelsMis.Domain.Enums;

namespace LabelsMis.Web.Pages.Production;

public record ProductionStageLink(string Page, string Label, JobStatus Status);

/// <summary>Minimal data the stage nav bar needs, so both the stage pages and the shipping workspace can render it.</summary>
public interface IProductionStageNav
{
    JobStatus Stage { get; }
    IReadOnlyDictionary<JobStatus, int> StageCounts { get; }
}

/// <summary>The ordered production stage pages, used to render the stage nav bar with counts.</summary>
public static class ProductionStages
{
    public static readonly IReadOnlyList<ProductionStageLink> All =
    [
        new("/Production/PrePress", "Pre-press", JobStatus.PrePress),
        new("/Production/Press", "Press", JobStatus.Queued),
        new("/Production/Finishing", "Finishing", JobStatus.Printed),
        new("/Production/Rewinding", "Rewinding", JobStatus.Finished),
        new("/Production/Shipping", "Shipping", JobStatus.Rewound),
    ];

    /// <summary>The next stage and its action label for a job in the given status, or null if there is none.</summary>
    public static (JobStatus Next, string Label)? NextStep(JobStatus status) => status switch
    {
        JobStatus.PrePress => (JobStatus.Queued, "Send to press"),
        JobStatus.Queued => (JobStatus.Printed, "Mark printed"),
        JobStatus.Printed => (JobStatus.Finished, "Mark finished"),
        JobStatus.Finished => (JobStatus.Rewound, "Mark rewound"),
        JobStatus.Rewound => (JobStatus.Shipped, "Mark shipped"),
        JobStatus.Shipped => (JobStatus.Closed, "Close job"),
        _ => null
    };
}
