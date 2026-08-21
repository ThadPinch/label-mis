using LabelsMis.Domain.Entities;

namespace LabelsMis.Domain.Tests.Entities;

public class JobNotesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void CreatePlanned_SeedsNotes()
    {
        var job = Job.CreatePlanned(Guid.NewGuid(), "JOB-2026-00001", Guid.NewGuid(), Guid.NewGuid(),
            5000, 5250, null, 5, "  from the order line  ", null, UserId, Now);

        job.Notes.Should().Be("from the order line");
    }

    [Fact]
    public void UpdateNotes_ReplacesAndClears()
    {
        var job = Job.CreatePlanned(Guid.NewGuid(), "JOB-2026-00001", Guid.NewGuid(), Guid.NewGuid(),
            5000, 5250, null, 5, "seeded", null, UserId, Now);

        job.UpdateNotes(" edited on the floor ", UserId, Now);
        job.Notes.Should().Be("edited on the floor");

        job.UpdateNotes("   ", UserId, Now);
        job.Notes.Should().BeNull();
    }
}
