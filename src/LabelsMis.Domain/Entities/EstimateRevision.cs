using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class EstimateRevision : EntityBase
{
    private EstimateRevision()
    {
    }

    public Guid EstimateId { get; private set; }
    public Estimate Estimate { get; private set; } = null!;
    public int RevisionNumber { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;

    public static EstimateRevision Create(
        Guid id,
        Guid estimateId,
        int revisionNumber,
        string snapshotJson,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            throw new ArgumentException("Snapshot JSON is required.", nameof(snapshotJson));
        }

        var revision = new EstimateRevision
        {
            EstimateId = estimateId,
            RevisionNumber = revisionNumber,
            SnapshotJson = snapshotJson
        };
        revision.SetCreated(id, createdById, createdAt);
        return revision;
    }
}
