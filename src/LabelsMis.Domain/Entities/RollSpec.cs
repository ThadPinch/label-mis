using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class RollSpec : EntityBase
{
    private RollSpec()
    {
    }

    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int LabelsPerRoll { get; private set; }
    public decimal CoreSizeIn { get; private set; }
    public int UnwindPosition { get; private set; }
    public decimal MaxOdIn { get; private set; }
    public int RollsPerCase { get; private set; }
    public string? CaseLabelFormat { get; private set; }

    public static RollSpec Create(
        Guid id,
        Guid productId,
        int labelsPerRoll,
        decimal coreSizeIn,
        int unwindPosition,
        decimal maxOdIn,
        int rollsPerCase,
        string? caseLabelFormat,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(labelsPerRoll, coreSizeIn, unwindPosition, maxOdIn, rollsPerCase);

        var rollSpec = new RollSpec
        {
            ProductId = productId,
            LabelsPerRoll = labelsPerRoll,
            CoreSizeIn = coreSizeIn,
            UnwindPosition = unwindPosition,
            MaxOdIn = maxOdIn,
            RollsPerCase = rollsPerCase,
            CaseLabelFormat = string.IsNullOrWhiteSpace(caseLabelFormat) ? null : caseLabelFormat.Trim()
        };
        rollSpec.SetCreated(id, createdById, createdAt);
        return rollSpec;
    }

    public void Update(
        int labelsPerRoll,
        decimal coreSizeIn,
        int unwindPosition,
        decimal maxOdIn,
        int rollsPerCase,
        string? caseLabelFormat,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        Validate(labelsPerRoll, coreSizeIn, unwindPosition, maxOdIn, rollsPerCase);

        LabelsPerRoll = labelsPerRoll;
        CoreSizeIn = coreSizeIn;
        UnwindPosition = unwindPosition;
        MaxOdIn = maxOdIn;
        RollsPerCase = rollsPerCase;
        CaseLabelFormat = string.IsNullOrWhiteSpace(caseLabelFormat) ? null : caseLabelFormat.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    private static void Validate(int labelsPerRoll, decimal coreSizeIn, int unwindPosition, decimal maxOdIn, int rollsPerCase)
    {
        if (labelsPerRoll <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelsPerRoll), "Labels per roll must be greater than zero.");
        }

        if (coreSizeIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coreSizeIn), "Core size must be greater than zero.");
        }

        if (unwindPosition is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(unwindPosition), "Unwind position must be between 1 and 8.");
        }

        if (maxOdIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOdIn), "Max OD must be greater than zero.");
        }

        if (rollsPerCase <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rollsPerCase), "Rolls per case must be greater than zero.");
        }
    }
}
