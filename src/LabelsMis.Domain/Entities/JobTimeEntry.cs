using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class JobTimeEntry : EntityBase
{
    private JobTimeEntry()
    {
    }

    public Guid JobOperationId { get; private set; }
    public JobOperation JobOperation { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public DateTime ClockedInAt { get; private set; }
    public DateTime? ClockedOutAt { get; private set; }

    public static JobTimeEntry Create(Guid id, Guid jobOperationId, Guid userId, DateTime clockedInAt)
    {
        var entry = new JobTimeEntry
        {
            JobOperationId = jobOperationId,
            UserId = userId,
            ClockedInAt = clockedInAt
        };
        entry.SetCreated(id, userId, clockedInAt);
        return entry;
    }

    public void ClockOut(DateTime clockedOutAt)
    {
        if (clockedOutAt < ClockedInAt)
        {
            throw new ArgumentOutOfRangeException(nameof(clockedOutAt));
        }

        ClockedOutAt = clockedOutAt;
    }

    public decimal DurationHours =>
        ClockedOutAt.HasValue
            ? (decimal)(ClockedOutAt.Value - ClockedInAt).TotalHours
            : 0m;
}
