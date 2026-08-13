using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class JobOperation : EntityBase
{
    private readonly List<JobTimeEntry> _timeEntries = [];

    private JobOperation()
    {
    }

    public Guid JobId { get; private set; }
    public Job Job { get; private set; } = null!;
    public int Sequence { get; private set; }
    public JobOperationType OperationType { get; private set; }
    public EquipmentType EquipmentType { get; private set; }
    public Guid? EquipmentId { get; private set; }
    public DateTime? PlannedStartAt { get; private set; }
    public decimal PlannedMinutes { get; private set; }
    public DateTime? ActualStartAt { get; private set; }
    public DateTime? ActualEndAt { get; private set; }
    public JobOperationStatus Status { get; private set; }
    public Guid? OperatorId { get; private set; }
    public int GoodCount { get; private set; }
    public int WasteCount { get; private set; }
    public decimal DowntimeMinutes { get; private set; }
    public DowntimeReasonCode? DowntimeReasonCode { get; private set; }

    /// <summary>Operator-recorded time the step actually took, in minutes. Zero means "not
    /// recorded" — costing then falls back to clock-on/off time entries.</summary>
    public decimal ActualMinutes { get; private set; }
    public Guid? ScannedRollId { get; private set; }

    public IReadOnlyCollection<JobTimeEntry> TimeEntries => _timeEntries;

    /// <summary>
    /// Hours actually spent on this operation: the recorded actual minutes when present,
    /// otherwise the sum of closed clock-on/off entries. Recorded minutes win so an operator's
    /// stated time replaces (rather than stacks on) any clocked time for the same step.
    /// </summary>
    public decimal ActualHours =>
        ActualMinutes > 0
            ? ActualMinutes / 60m
            : _timeEntries.Where(t => t.ClockedOutAt.HasValue).Sum(t => t.DurationHours);

    public static JobOperation Create(
        Guid id,
        Guid jobId,
        int sequence,
        JobOperationType operationType,
        EquipmentType equipmentType,
        Guid? equipmentId,
        decimal plannedMinutes,
        Guid createdById,
        DateTime createdAt)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        var operation = new JobOperation
        {
            JobId = jobId,
            Sequence = sequence,
            OperationType = operationType,
            EquipmentType = equipmentType,
            EquipmentId = equipmentId,
            PlannedMinutes = plannedMinutes,
            Status = JobOperationStatus.Pending
        };
        operation.SetCreated(id, createdById, createdAt);
        return operation;
    }

    public void Start(Guid operatorId, DateTime startedAt, Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is JobOperationStatus.Complete or JobOperationStatus.Skipped)
        {
            throw new InvalidOperationException("Completed operations cannot be started.");
        }

        Status = JobOperationStatus.InProgress;
        OperatorId = operatorId;
        ActualStartAt ??= startedAt;
        SetModified(modifiedById, modifiedAt);
    }

    public JobTimeEntry ClockOn(Guid entryId, Guid userId, DateTime clockedInAt)
    {
        if (Status is JobOperationStatus.Complete or JobOperationStatus.Skipped)
        {
            throw new InvalidOperationException("Cannot clock on to a completed operation.");
        }

        if (_timeEntries.Any(t => t.ClockedOutAt is null))
        {
            throw new InvalidOperationException("Operator is already clocked on.");
        }

        var entry = JobTimeEntry.Create(entryId, Id, userId, clockedInAt);
        _timeEntries.Add(entry);
        return entry;
    }

    public void ClockOff(
        Guid userId,
        DateTime clockedOutAt,
        int goodCount,
        int wasteCount,
        decimal downtimeMinutes,
        DowntimeReasonCode? downtimeReason,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        var openEntry = _timeEntries.FirstOrDefault(t => t.UserId == userId && t.ClockedOutAt is null)
            ?? throw new InvalidOperationException("No open time entry found. Clock on first.");

        openEntry.ClockOut(clockedOutAt);
        GoodCount += goodCount;
        WasteCount += wasteCount;
        DowntimeMinutes += downtimeMinutes;
        if (downtimeMinutes > 0)
        {
            DowntimeReasonCode = downtimeReason;
        }

        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>
    /// Sets the recorded production counts to absolute values — the job-page "record / adjust
    /// counts" action. Unlike <see cref="ClockOff"/> (which accumulates per shift), this replaces
    /// the totals, so operators can record a fresh round or correct earlier numbers whenever the
    /// job passes through this step again.
    /// </summary>
    public void SetCounts(
        int goodCount,
        int wasteCount,
        decimal downtimeMinutes,
        DowntimeReasonCode? downtimeReason,
        decimal actualMinutes,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (goodCount < 0 || wasteCount < 0 || downtimeMinutes < 0 || actualMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(goodCount), "Counts cannot be negative.");
        }

        GoodCount = goodCount;
        WasteCount = wasteCount;
        DowntimeMinutes = downtimeMinutes;
        DowntimeReasonCode = downtimeMinutes > 0 ? downtimeReason : null;
        ActualMinutes = actualMinutes;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>
    /// Records how long the step actually took as an absolute value, replacing any earlier
    /// recording. An entry below the planned time is how operators report a step ran faster
    /// than estimated (a favorable variance).
    /// </summary>
    public void RecordActualMinutes(decimal actualMinutes, Guid modifiedById, DateTime modifiedAt)
    {
        if (actualMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualMinutes), "Time cannot be negative.");
        }

        ActualMinutes = actualMinutes;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>
    /// Replans the expected duration — used when the job's spec or quantity changes. Only pending
    /// operations can be replanned; once work has started the plan is history, not a forecast.
    /// </summary>
    public void UpdatePlannedMinutes(decimal plannedMinutes, Guid modifiedById, DateTime modifiedAt)
    {
        if (Status != JobOperationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending operations can be replanned.");
        }

        PlannedMinutes = plannedMinutes;
        SetModified(modifiedById, modifiedAt);
    }

    public void Complete(Guid modifiedById, DateTime modifiedAt)
    {
        if (_timeEntries.Any(t => t.ClockedOutAt is null))
        {
            throw new InvalidOperationException("Cannot complete operation with open time entries.");
        }

        Status = JobOperationStatus.Complete;
        ActualEndAt = modifiedAt;
        SetModified(modifiedById, modifiedAt);
    }

    public void Skip(Guid modifiedById, DateTime modifiedAt)
    {
        Status = JobOperationStatus.Skipped;
        SetModified(modifiedById, modifiedAt);
    }

    public void LinkRoll(Guid rollId, Guid modifiedById, DateTime modifiedAt)
    {
        ScannedRollId = rollId;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddTimeEntry(JobTimeEntry entry) => _timeEntries.Add(entry);
}
