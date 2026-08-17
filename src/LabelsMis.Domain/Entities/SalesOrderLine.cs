using LabelsMis.Domain.Common;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

public class SalesOrderLine : EntityBase
{
    private SalesOrderLine()
    {
    }

    public Guid SalesOrderId { get; private set; }
    public SalesOrder SalesOrder { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;

    /// <summary>Snapshot of the description as quoted (estimate lines can reword an existing
    /// product's description). Null on rows created before the snapshot existed — display should
    /// fall back to Product.Description.</summary>
    public string? Description { get; private set; }

    public Guid? SourceEstimateLineId { get; private set; }
    public EstimateLine? SourceEstimateLine { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public string? LineNotes { get; private set; }

    /// <summary>Snapshot of the ordered spec, copied from the estimate line (or seeded from the
    /// product on direct orders). Nullable only for rows created before the LabelSpec refactor and
    /// awaiting backfill — new lines always carry it. See docs/labelspec-refactor.md.</summary>
    public LabelSpec? Spec { get; private set; }

    /// <summary>Set when this line is bought from an outside vendor instead of run in-house.</summary>
    public OutsourcedItem? OutsourcedItem { get; private set; }
    public bool IsOutsourced => OutsourcedItem is not null;

    public static SalesOrderLine Create(
        Guid id,
        Guid salesOrderId,
        int lineNumber,
        Guid productId,
        string? description,
        Guid? sourceEstimateLineId,
        int quantity,
        decimal unitPrice,
        string? lineNotes,
        LabelSpec? spec,
        Guid createdById,
        DateTime createdAt)
    {
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line number must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        var line = new SalesOrderLine
        {
            SalesOrderId = salesOrderId,
            LineNumber = lineNumber,
            ProductId = productId,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SourceEstimateLineId = sourceEstimateLineId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity,
            LineNotes = string.IsNullOrWhiteSpace(lineNotes) ? null : lineNotes.Trim(),
            Spec = spec
        };
        line.SetCreated(id, createdById, createdAt);
        return line;
    }

    /// <summary>Replaces the ordered spec (CSR edit while the order is open, or one-time backfill).</summary>
    public void SetSpec(LabelSpec spec, Guid modifiedById, DateTime modifiedAt)
    {
        Spec = spec;
        SetModified(modifiedById, modifiedAt);
    }

    public void Update(int quantity, decimal unitPrice, string? lineNotes, Guid modifiedById, DateTime modifiedAt)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = unitPrice * quantity;
        LineNotes = string.IsNullOrWhiteSpace(lineNotes) ? null : lineNotes.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void UpdateDescription(string? description, Guid modifiedById, DateTime modifiedAt)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SetModified(modifiedById, modifiedAt);
    }
}
