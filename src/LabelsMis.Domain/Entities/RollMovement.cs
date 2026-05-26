using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class RollMovement : EntityBase
{
    private RollMovement()
    {
    }

    public Guid RollId { get; private set; }
    public Roll Roll { get; private set; } = null!;
    public RollMovementType MovementType { get; private set; }
    public decimal QuantityLf { get; private set; }
    public Guid? JobId { get; private set; }
    public Job? Job { get; private set; }
    public DateTime MovedAt { get; private set; }
    public string? Notes { get; private set; }

    public static RollMovement Create(
        Guid id,
        Guid rollId,
        RollMovementType movementType,
        decimal quantityLf,
        Guid? jobId,
        DateTime movedAt,
        string? notes,
        Guid movedById,
        DateTime createdAt)
    {
        var movement = new RollMovement
        {
            RollId = rollId,
            MovementType = movementType,
            QuantityLf = quantityLf,
            JobId = jobId,
            MovedAt = movedAt,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        movement.SetCreated(id, movedById, createdAt);
        return movement;
    }
}
