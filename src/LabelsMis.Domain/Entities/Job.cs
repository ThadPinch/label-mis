using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

public class Job : EntityBase
{
    private readonly List<JobOperation> _operations = [];
    private readonly List<JobMaterialUsage> _materialUsages = [];

    private Job()
    {
    }

    public string JobNumber { get; private set; } = string.Empty;
    public Guid SalesOrderLineId { get; private set; }
    public SalesOrderLine SalesOrderLine { get; private set; } = null!;
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int QuantityOrdered { get; private set; }
    public int QuantityPlanned { get; private set; }
    public JobStatus Status { get; private set; }
    public DateOnly? ScheduledForDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public int Priority { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ScheduledPressId { get; private set; }

    /// <summary>Snapshot of the spec this job runs — copied from the order line at scheduling and
    /// editable on the floor. Nullable only for pre-refactor rows awaiting backfill.</summary>
    public LabelSpec? Spec { get; private set; }

    public IReadOnlyCollection<JobOperation> Operations => _operations;
    public IReadOnlyCollection<JobMaterialUsage> MaterialUsages => _materialUsages;

    public static Job CreatePlanned(
        Guid id,
        string jobNumber,
        Guid salesOrderLineId,
        Guid productId,
        int quantityOrdered,
        int quantityPlanned,
        DateOnly? dueDate,
        int priority,
        string? notes,
        LabelSpec? spec,
        Guid createdById,
        DateTime createdAt)
    {
        if (quantityOrdered <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityOrdered));
        }

        if (quantityPlanned < quantityOrdered)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityPlanned), "Planned quantity must cover ordered quantity.");
        }

        var job = new Job
        {
            JobNumber = jobNumber,
            SalesOrderLineId = salesOrderLineId,
            ProductId = productId,
            QuantityOrdered = quantityOrdered,
            QuantityPlanned = quantityPlanned,
            Status = JobStatus.PrePress,
            DueDate = dueDate,
            Priority = priority,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Spec = spec
        };
        job.SetCreated(id, createdById, createdAt);
        return job;
    }

    /// <summary>Replaces the job's spec — a production edit on the floor, or one-time backfill.</summary>
    public void SetSpec(LabelSpec spec, Guid modifiedById, DateTime modifiedAt)
    {
        Spec = spec;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>
    /// Follows an unlocked sales-order edit that changed the line quantity. Planned quantity keeps
    /// its overrun above the old ordered quantity when possible, and never drops below ordered.
    /// </summary>
    public void UpdateOrderedQuantity(int quantityOrdered, Guid modifiedById, DateTime modifiedAt)
    {
        if (quantityOrdered <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityOrdered));
        }

        var overrun = Math.Max(0, QuantityPlanned - QuantityOrdered);
        QuantityOrdered = quantityOrdered;
        QuantityPlanned = quantityOrdered + overrun;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Assigns a production date/press. Status moves through the stages explicitly, not here.</summary>
    public void Schedule(DateOnly scheduledForDate, Guid? pressId, Guid modifiedById, DateTime modifiedAt)
    {
        ScheduledForDate = scheduledForDate;
        ScheduledPressId = pressId;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddOperation(JobOperation operation) => _operations.Add(operation);

    public void ReplaceOperations(IEnumerable<JobOperation> operations)
    {
        _operations.Clear();
        _operations.AddRange(operations);
    }

    public void AddMaterialUsage(JobMaterialUsage usage) => _materialUsages.Add(usage);

    public void AdvanceStatus(JobStatus status, Guid modifiedById, DateTime modifiedAt)
    {
        if (status <= Status)
        {
            throw new InvalidOperationException("Job status can only move forward.");
        }

        Status = status;
        SetModified(modifiedById, modifiedAt);
    }

    public void SetStatus(JobStatus status, Guid modifiedById, DateTime modifiedAt)
    {
        Status = status;
        SetModified(modifiedById, modifiedAt);
    }

    public void Close(Guid modifiedById, DateTime modifiedAt)
    {
        if (_operations.Any(o => o.Status is JobOperationStatus.InProgress))
        {
            throw new InvalidOperationException("Job cannot be closed while an operation is in progress.");
        }

        if (_operations.Any(o => o.Status is JobOperationStatus.Pending))
        {
            throw new InvalidOperationException("Job cannot be closed while operations are pending.");
        }

        Status = JobStatus.Closed;
        SetModified(modifiedById, modifiedAt);
    }

    public int TotalGoodCount => _operations.Sum(o => o.GoodCount);
    public int TotalWasteCount => _operations.Sum(o => o.WasteCount);
}
