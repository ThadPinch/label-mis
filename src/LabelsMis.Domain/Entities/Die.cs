using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Die : MasterDataEntity
{
    private readonly List<DieUsage> _usageHistory = [];

    private Die()
    {
    }

    public string Description { get; private set; } = string.Empty;
    public Guid? CustomerId { get; private set; }
    public Customer? Customer { get; private set; }
    public DieType DieType { get; private set; }
    public string? Shape { get; private set; }
    public decimal LabelAcrossIn { get; private set; }
    public decimal LabelAroundIn { get; private set; }
    public decimal CornerRadiusIn { get; private set; }
    public decimal GutterAcrossIn { get; private set; }
    public decimal GutterAroundIn { get; private set; }
    public int LabelsAcross { get; private set; }
    public int LabelsAround { get; private set; }
    public decimal RepeatLengthIn { get; private set; }
    public decimal WebWidthIn { get; private set; }
    public Guid? SupplierId { get; private set; }
    public Supplier? Supplier { get; private set; }
    public string? SupplierPartNumber { get; private set; }
    public string? Location { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public int UsageCount { get; private set; }
    public DateTime? RetiredAt { get; private set; }

    public IReadOnlyCollection<DieUsage> UsageHistory => _usageHistory;

    public static Die Create(
        Guid id,
        string description,
        Guid? customerId,
        DieType dieType,
        string? shape,
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        decimal gutterAcrossIn,
        decimal gutterAroundIn,
        int labelsAcross,
        int labelsAround,
        decimal webWidthIn,
        Guid? supplierId,
        string? supplierPartNumber,
        string? location,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Die description is required.", nameof(description));
        }

        if (labelAcrossIn <= 0 || labelAroundIn <= 0 || webWidthIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelAcrossIn), "Label dimensions and web width must be greater than zero.");
        }

        if (labelsAcross <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelsAcross), "Labels across must be at least one.");
        }

        if (labelsAround <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelsAround), "Labels around must be at least one.");
        }

        var repeatLengthIn = labelsAround * (labelAroundIn + gutterAroundIn);

        var die = new Die
        {
            Description = description.Trim(),
            CustomerId = customerId,
            DieType = dieType,
            Shape = string.IsNullOrWhiteSpace(shape) ? null : shape.Trim(),
            LabelAcrossIn = labelAcrossIn,
            LabelAroundIn = labelAroundIn,
            CornerRadiusIn = cornerRadiusIn,
            GutterAcrossIn = gutterAcrossIn,
            GutterAroundIn = gutterAroundIn,
            LabelsAcross = labelsAcross,
            LabelsAround = labelsAround,
            RepeatLengthIn = repeatLengthIn,
            WebWidthIn = webWidthIn,
            SupplierId = supplierId,
            SupplierPartNumber = string.IsNullOrWhiteSpace(supplierPartNumber) ? null : supplierPartNumber.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim()
        };
        die.SetCreated(id, createdById, createdAt);
        return die;
    }

    public void UpdateImposition(
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        decimal gutterAcrossIn,
        decimal gutterAroundIn,
        int labelsAcross,
        int labelsAround,
        decimal webWidthIn,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (labelAcrossIn <= 0 || labelAroundIn <= 0 || webWidthIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelAcrossIn), "Label dimensions and web width must be greater than zero.");
        }

        if (labelsAcross <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelsAcross), "Labels across must be at least one.");
        }

        if (labelsAround <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelsAround), "Labels around must be at least one.");
        }

        LabelAcrossIn = labelAcrossIn;
        LabelAroundIn = labelAroundIn;
        CornerRadiusIn = cornerRadiusIn;
        GutterAcrossIn = gutterAcrossIn;
        GutterAroundIn = gutterAroundIn;
        LabelsAcross = labelsAcross;
        LabelsAround = labelsAround;
        WebWidthIn = webWidthIn;
        RepeatLengthIn = labelsAround * (labelAroundIn + gutterAroundIn);
        SetModified(modifiedById, modifiedAt);
    }

    public void UpdateDetails(
        string description,
        Guid? customerId,
        DieType dieType,
        string? shape,
        Guid? supplierId,
        string? supplierPartNumber,
        string? location,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Die description is required.", nameof(description));
        }

        Description = description.Trim();
        CustomerId = customerId;
        DieType = dieType;
        Shape = string.IsNullOrWhiteSpace(shape) ? null : shape.Trim();
        SupplierId = supplierId;
        SupplierPartNumber = string.IsNullOrWhiteSpace(supplierPartNumber) ? null : supplierPartNumber.Trim();
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Geometric best-fit for reference; the die's stored LabelsAcross is user-entered
    /// (the physical cavity count can differ from the pure width calculation).</summary>
    public static int CalculateLabelsAcross(decimal webWidthIn, decimal gutterAcrossIn, decimal labelAcrossIn)
    {
        if (labelAcrossIn + gutterAcrossIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelAcrossIn), "Label width plus gutter must be greater than zero.");
        }

        return (int)Math.Floor((webWidthIn + gutterAcrossIn) / (labelAcrossIn + gutterAcrossIn));
    }

    public DieUsage RecordUsage(
        Guid usageId,
        Guid? jobId,
        DateTime usedAt,
        Guid usedById,
        string? notes,
        DateTime recordedAt)
    {
        UsageCount++;
        LastUsedAt = usedAt;
        SetModified(usedById, recordedAt);

        var usage = DieUsage.Create(usageId, Id, jobId, usedAt, usedById, notes, usedById, recordedAt);
        _usageHistory.Add(usage);
        return usage;
    }

    public void AddUsage(DieUsage usage) => _usageHistory.Add(usage);
}
