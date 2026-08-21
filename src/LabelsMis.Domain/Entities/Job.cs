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

    /// <summary>The line is made by an outside vendor: the job carries no press/finishing operations,
    /// waits in <see cref="JobStatus.Outsourced"/>, and moves to Rewound (ready to ship) on receipt.</summary>
    public bool IsOutsourced { get; private set; }

    /// <summary>How this job's artwork is stepped onto a press frame. Null until prepress first saves
    /// or runs the imposition — callers seed a default from the spec/estimate layout meanwhile.</summary>
    public ImpositionTemplate? Imposition { get; private set; }

    /// <summary>Storage key of the last imposed (step-and-repeat) PDF, generated from
    /// <see cref="ImposedFromArtworkFilePath"/> at <see cref="ImposedAt"/>. The product's own artwork
    /// stays the unimposed original.</summary>
    public string? ImposedArtworkFilePath { get; private set; }
    public DateTime? ImposedAt { get; private set; }
    public string? ImposedFromArtworkFilePath { get; private set; }

    /// <summary>The imposed PDF was uploaded by hand rather than generated from the template — the
    /// template inputs no longer describe it, and it never counts as "stale".</summary>
    public bool ImposedIsManual { get; private set; }

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

    /// <summary>Routes a freshly planned job to the vendor instead of pre-press.</summary>
    public void MarkOutsourced(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is not JobStatus.PrePress || _operations.Count > 0)
        {
            throw new InvalidOperationException("Only a newly planned job with no operations can be marked outsourced.");
        }

        IsOutsourced = true;
        Status = JobStatus.Outsourced;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>The outsourced goods are in: the job is ready to ship (Rewound), skipping press and finishing.</summary>
    public void ReceiveOutsourced(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is not JobStatus.Outsourced)
        {
            throw new InvalidOperationException("Only a job that is at the vendor can be received.");
        }

        Status = JobStatus.Rewound;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Edits this job's own notes (seeded from the order line at scheduling). Allowed in any status.</summary>
    public void UpdateNotes(string? notes, Guid modifiedById, DateTime modifiedAt)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Replaces the job's spec — a production edit on the floor, or one-time backfill.</summary>
    public void SetSpec(LabelSpec spec, Guid modifiedById, DateTime modifiedAt)
    {
        Spec = spec;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Saves the imposition template prepress will run (does not touch the spec).</summary>
    public void SetImposition(ImpositionTemplate template, Guid modifiedById, DateTime modifiedAt)
    {
        Imposition = template;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Records a freshly generated imposed PDF and the artwork it was built from.</summary>
    public void RecordImposedArtwork(string storageKey, string sourceArtworkKey, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Imposed artwork storage key is required.", nameof(storageKey));
        }

        ImposedArtworkFilePath = storageKey.Trim();
        ImposedFromArtworkFilePath = string.IsNullOrWhiteSpace(sourceArtworkKey) ? null : sourceArtworkKey.Trim();
        ImposedAt = modifiedAt;
        ImposedIsManual = false;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Records a hand-uploaded imposed PDF (not generated from the template).</summary>
    public void RecordManualImposedArtwork(string storageKey, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Imposed artwork storage key is required.", nameof(storageKey));
        }

        ImposedArtworkFilePath = storageKey.Trim();
        ImposedFromArtworkFilePath = null;
        ImposedAt = modifiedAt;
        ImposedIsManual = true;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Removes the imposed PDF reference (the template is kept). The stored file is deleted
    /// by the caller.</summary>
    public void ClearImposedArtwork(Guid modifiedById, DateTime modifiedAt)
    {
        ImposedArtworkFilePath = null;
        ImposedFromArtworkFilePath = null;
        ImposedAt = null;
        ImposedIsManual = false;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>The generated imposed PDF was built from a different artwork file than the product now
    /// carries. A hand-uploaded imposition is never stale (it isn't derived from the product artwork).</summary>
    public bool ImposedArtworkIsStale(string? currentArtworkKey) =>
        ImposedArtworkFilePath is not null
        && !ImposedIsManual
        && !string.Equals(ImposedFromArtworkFilePath, currentArtworkKey, StringComparison.Ordinal);

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
