using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class DieUsage : EntityBase
{
    private DieUsage()
    {
    }

    public Guid DieId { get; private set; }
    public Die Die { get; private set; } = null!;
    public Guid? JobId { get; private set; }
    public DateTime UsedAt { get; private set; }
    public Guid UsedById { get; private set; }
    public string? Notes { get; private set; }

    public static DieUsage Create(
        Guid id,
        Guid dieId,
        Guid? jobId,
        DateTime usedAt,
        Guid usedById,
        string? notes,
        Guid createdById,
        DateTime createdAt)
    {
        var usage = new DieUsage
        {
            DieId = dieId,
            JobId = jobId,
            UsedAt = usedAt,
            UsedById = usedById,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        usage.SetCreated(id, createdById, createdAt);
        return usage;
    }
}
