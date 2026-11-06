using LabelsMis.Domain.Jobs;

namespace LabelsMis.Domain.Tests.Jobs;

public class JobDueTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Theory]
    [InlineData(null, DueUrgency.None)]
    [InlineData(-3, DueUrgency.Overdue)]
    [InlineData(-1, DueUrgency.Overdue)]
    [InlineData(0, DueUrgency.Today)]
    [InlineData(1, DueUrgency.Tomorrow)]
    [InlineData(2, DueUrgency.ThisWeek)]
    [InlineData(7, DueUrgency.ThisWeek)]
    [InlineData(8, DueUrgency.Later)]
    public void Classify_ByDaysFromToday(int? daysFromToday, DueUrgency expected)
    {
        DateOnly? due = daysFromToday is { } d ? Today.AddDays(d) : null;

        JobDue.Classify(due, Today).Should().Be(expected);
    }

    [Fact]
    public void OrderForFloor_SoonestDueFirstAndUndatedLast()
    {
        var jobs = new (string Number, DateOnly? Due)[]
        {
            ("JOB-4", null),
            ("JOB-3", Today.AddDays(3)),
            ("JOB-1", Today.AddDays(-2)),
            ("JOB-5", Today.AddDays(3)),
            ("JOB-2", Today),
        };

        JobDue.OrderForFloor(jobs, j => j.Due, j => j.Number)
            .Select(j => j.Number)
            .Should().Equal("JOB-1", "JOB-2", "JOB-3", "JOB-5", "JOB-4");
    }
}
