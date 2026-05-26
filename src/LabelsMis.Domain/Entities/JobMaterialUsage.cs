using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class JobMaterialUsage : EntityBase
{
    private JobMaterialUsage()
    {
    }

    public Guid JobId { get; private set; }
    public Job Job { get; private set; } = null!;
    public Guid StockId { get; private set; }
    public Stock Stock { get; private set; } = null!;
    public Guid? RollId { get; private set; }
    public Roll? Roll { get; private set; }
    public decimal QuantityUsedLf { get; private set; }
    public DateTime UsedAt { get; private set; }
    public string? Notes { get; private set; }

    public static JobMaterialUsage Create(
        Guid id,
        Guid jobId,
        Guid stockId,
        Guid? rollId,
        decimal quantityUsedLf,
        DateTime usedAt,
        string? notes,
        Guid usedById,
        DateTime createdAt)
    {
        if (quantityUsedLf <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityUsedLf));
        }

        var usage = new JobMaterialUsage
        {
            JobId = jobId,
            StockId = stockId,
            RollId = rollId,
            QuantityUsedLf = quantityUsedLf,
            UsedAt = usedAt,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        usage.SetCreated(id, usedById, createdAt);
        return usage;
    }
}
