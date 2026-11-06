using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;

namespace LabelsMis.Web.Pages.Jobs;

/// <summary>The text the floor's find box matches a job against (job-floor.js reads it from
/// <c>data-search</c>): every field the board shows, lower-cased.</summary>
public static class FloorText
{
    public static string For(FloorJob job) =>
        string.Join(' ', new[]
            {
                job.JobNumber,
                job.CustomerName,
                job.ProductDescription,
                job.ProductSku,
                job.CustomerSku,
                job.OrderNumber,
                job.CustomerPoNumber is { Length: > 0 } po ? $"PO {po}" : null,
                job.Location.StageLabel,
                job.Location.Where,
                job.Location.Detail,
                job.Location.RunningBy,
                job.ShippingMethodName
            }
            .Where(s => !string.IsNullOrWhiteSpace(s)))
        .ToLowerInvariant();

    /// <summary>CSS key for a stage's colour (<c>stage-finishing</c>) and the board's data-stage.</summary>
    public static string StageKey(JobStatus status) => status switch
    {
        JobStatus.Outsourced => "outsourced",
        JobStatus.PrePress => "prepress",
        JobStatus.Queued => "press",
        JobStatus.Printed => "finishing",
        JobStatus.Finished => "rewinding",
        JobStatus.Rewound => "shipping",
        JobStatus.Shipped => "shipped",
        JobStatus.Closed => "closed",
        _ => status.ToString().ToLowerInvariant()
    };

    /// <summary>"JOB-2026-00057" → ("JOB-2026-", "00057"): the board greys the prefix so the
    /// sequence number people say out loud stands out.</summary>
    public static (string Prefix, string Tail) SplitNumber(string number)
    {
        var dash = number.LastIndexOf('-');
        return dash > 0 && dash < number.Length - 1 ? (number[..(dash + 1)], number[(dash + 1)..]) : ("", number);
    }
}
