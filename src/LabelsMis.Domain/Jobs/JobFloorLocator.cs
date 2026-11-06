using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Jobs;

public enum FloorStepState
{
    Done,
    Current,
    Upcoming
}

/// <summary>One step of a job's route as the floor board draws it (a dot in the progress row).</summary>
public sealed record FloorStep(string Label, FloorStepState State, bool Running = false);

/// <summary>A route operation, already labelled by the caller ("Press", the finishing operation's
/// description, "QC inspection", …). <paramref name="ClockedOnBy"/> names whoever has an open
/// time entry on it.</summary>
public sealed record FloorOperation(
    int Sequence,
    JobOperationType Type,
    JobOperationStatus Status,
    string Label,
    string? ClockedOnBy = null)
{
    public bool IsDone => Status is JobOperationStatus.Complete or JobOperationStatus.Skipped;

    public bool IsRunning => Status is JobOperationStatus.InProgress || ClockedOnBy is not null;
}

/// <summary>Everything the floor locator reads about one job.</summary>
public sealed record JobFloorFacts(
    JobStatus Status,
    bool IsOutsourced,
    IReadOnlyList<FloorOperation> Operations,
    bool IsImposed = false,
    bool ImposedIsStale = false,
    string? PressName = null,
    string? VendorName = null,
    DateOnly? VendorExpectedIn = null,
    string? ShippingMethodName = null,
    bool ShipmentRecordRequired = false);

/// <summary>
/// Where a job is on the floor right now: its stage (the production stage page it is listed on),
/// the station or step within that stage ("Laminate", "Queued for HP Indigo 6800", "At Acme
/// Flexo"), a short detail line, whether someone is on it, and its whole route as steps.
/// </summary>
public sealed record JobFloorLocation(
    JobStatus Stage,
    string StageLabel,
    string Where,
    string? Detail,
    bool IsRunning,
    string? RunningBy,
    bool NeedsAttention,
    IReadOnlyList<FloorStep> Route)
{
    public int StepsDone => Route.Count(s => s.State == FloorStepState.Done);
}

/// <summary>Works out a job's place on the floor from its status and route operations.</summary>
public static class JobFloorLocator
{
    /// <summary>The operation types worked in each production stage (the job status the stage
    /// page lists). Pre-press has no operation; outsourced work happens at the vendor.</summary>
    public static IReadOnlyList<JobOperationType> StageOperationTypes(JobStatus status) => status switch
    {
        JobStatus.Queued => [JobOperationType.Press],
        JobStatus.Printed => [JobOperationType.Finishing],
        JobStatus.Finished => [JobOperationType.Inspection],
        JobStatus.Rewound => [JobOperationType.Pack, JobOperationType.Ship],
        _ => []
    };

    /// <summary>Stage names as the production stage pages and nav call them.</summary>
    public static string StageLabel(JobStatus status) => status switch
    {
        JobStatus.Outsourced => "Outsourced",
        JobStatus.PrePress => "Pre-press",
        JobStatus.Queued => "Press",
        JobStatus.Printed => "Finishing",
        JobStatus.Finished => "Rewinding",
        JobStatus.Rewound => "Shipping",
        JobStatus.Shipped => "Shipped",
        JobStatus.Closed => "Closed",
        _ => status.ToString()
    };

    public static JobFloorLocation Locate(JobFloorFacts facts, DateOnly today)
    {
        var operations = facts.Operations.OrderBy(o => o.Sequence).ToList();
        var stageTypes = StageOperationTypes(facts.Status);
        var current = operations.FirstOrDefault(o => !o.IsDone && stageTypes.Contains(o.Type));
        var running = operations.FirstOrDefault(o => o.IsRunning && !o.IsDone);

        var route = BuildRoute(facts, operations, current);
        var (where, detail, attention) = Describe(facts, operations, current, running, today);

        // A running step outside the current stage (status set back by hand) still means
        // someone is on the job; report it rather than hide it.
        return new JobFloorLocation(
            facts.Status,
            StageLabel(facts.Status),
            where,
            detail,
            running is not null,
            running?.ClockedOnBy,
            attention,
            route);
    }

    private static List<FloorStep> BuildRoute(
        JobFloorFacts facts,
        IReadOnlyList<FloorOperation> operations,
        FloorOperation? current)
    {
        var route = new List<FloorStep>(operations.Count + 1);

        // The step before the route's first operation: the vendor for outsourced work, pre-press
        // (artwork + imposition) for everything made in house.
        var firstStage = facts.IsOutsourced ? JobStatus.Outsourced : JobStatus.PrePress;
        var firstLabel = facts.IsOutsourced
            ? (facts.VendorName is { } vendor ? $"Vendor — {vendor}" : "Vendor")
            : "Pre-press";
        var firstState = facts.Status == firstStage
            ? FloorStepState.Current
            : facts.Status > firstStage ? FloorStepState.Done : FloorStepState.Upcoming;
        route.Add(new FloorStep(firstLabel, firstState));

        foreach (var operation in operations)
        {
            var state = operation.IsDone
                ? FloorStepState.Done
                : ReferenceEquals(operation, current) ? FloorStepState.Current : FloorStepState.Upcoming;
            var label = operation.Type == JobOperationType.Press && facts.PressName is { } press
                ? $"Press — {press}"
                : operation.Label;
            route.Add(new FloorStep(label, state, operation.IsRunning && !operation.IsDone));
        }

        return route;
    }

    private static (string Where, string? Detail, bool Attention) Describe(
        JobFloorFacts facts,
        IReadOnlyList<FloorOperation> operations,
        FloorOperation? current,
        FloorOperation? running,
        DateOnly today)
    {
        switch (facts.Status)
        {
            case JobStatus.Outsourced:
            {
                var where = facts.VendorName is { } vendor ? $"At {vendor}" : "At the vendor";
                if (facts.VendorExpectedIn is not { } expected)
                {
                    return (where, "No expected-in date", false);
                }

                return expected < today
                    ? (where, $"Expected {expected:MMM d} — late", true)
                    : (where, $"Expected {expected:MMM d}", false);
            }

            case JobStatus.PrePress:
                if (facts.ImposedIsStale)
                {
                    return ("Re-run imposition", "Artwork changed after it was imposed", true);
                }

                return facts.IsImposed
                    ? ("Imposed — ready for press", null, false)
                    : ("Needs imposition", null, false);

            case JobStatus.Queued:
            {
                var press = facts.PressName ?? "the press";
                return current is { IsRunning: true }
                    ? ($"Printing on {press}", null, false)
                    : ($"Queued for {press}", null, false);
            }

            case JobStatus.Printed:
            {
                var finishing = operations.Where(o => o.Type == JobOperationType.Finishing).ToList();
                if (finishing.Count == 0)
                {
                    return ("Ready for finishing", "No finishing steps on the route", false);
                }

                if (current is null)
                {
                    return ("Finishing done", "Ready to mark finished", false);
                }

                var done = finishing.Count(o => o.IsDone);
                var detail = finishing.Count == 1 ? null : $"Step {done + 1} of {finishing.Count}";
                return (current.Label, detail, false);
            }

            case JobStatus.Finished:
                return ("Rewind & QC", current is { IsRunning: true } ? "In progress" : null, false);

            case JobStatus.Rewound:
            {
                var method = facts.ShippingMethodName;
                string? detail = (method, facts.ShipmentRecordRequired) switch
                {
                    ({ } m, true) => $"{m} · record the shipment",
                    ({ } m, false) => m,
                    (null, true) => "No shipping method · record the shipment",
                    _ => null
                };
                return ("Ready to ship", detail, false);
            }

            case JobStatus.Shipped:
                return ("Shipped", null, false);

            case JobStatus.Closed:
                return ("Closed", null, false);

            default:
                return (StageLabel(facts.Status), running?.Label, false);
        }
    }
}
