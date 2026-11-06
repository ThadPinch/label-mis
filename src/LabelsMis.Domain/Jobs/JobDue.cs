namespace LabelsMis.Domain.Jobs;

/// <summary>How close a job is to its due date, for the floor board's flags and due filters.</summary>
public enum DueUrgency
{
    /// <summary>No due date set.</summary>
    None,
    Overdue,
    Today,
    Tomorrow,

    /// <summary>Due within the next seven days (after tomorrow).</summary>
    ThisWeek,
    Later
}

public static class JobDue
{
    public static DueUrgency Classify(DateOnly? dueDate, DateOnly today)
    {
        if (dueDate is not { } due)
        {
            return DueUrgency.None;
        }

        if (due < today)
        {
            return DueUrgency.Overdue;
        }

        if (due == today)
        {
            return DueUrgency.Today;
        }

        if (due == today.AddDays(1))
        {
            return DueUrgency.Tomorrow;
        }

        return due <= today.AddDays(7) ? DueUrgency.ThisWeek : DueUrgency.Later;
    }

    /// <summary>The order the board lists a stage's jobs in: overdue first, then soonest due, then
    /// jobs with no due date; ties by job number.</summary>
    public static IOrderedEnumerable<T> OrderForFloor<T>(
        IEnumerable<T> jobs,
        Func<T, DateOnly?> dueDate,
        Func<T, string> jobNumber) =>
        jobs
            .OrderBy(j => dueDate(j) is null)
            .ThenBy(j => dueDate(j) ?? DateOnly.MaxValue)
            .ThenBy(jobNumber, StringComparer.Ordinal);
}
