using FluentAssertions;
using LabelsMis.Domain.Entities;

namespace LabelsMis.Domain.Tests.Entities;

/// <summary>The imposed-artwork state transitions the job page's Run / Upload / Delete flows rely on.</summary>
public class JobImpositionTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

    private static Job CreateJob() =>
        Job.CreatePlanned(Guid.NewGuid(), "JOB-2026-00001", Guid.NewGuid(), Guid.NewGuid(),
            5000, 5250, null, 5, null, null, UserId, Now);

    [Fact]
    public void RecordImposedArtwork_StoresGeneratedFrameAndSource()
    {
        var job = CreateJob();

        job.RecordImposedArtwork("imposed/frame.pdf", "artwork/source.pdf", UserId, Now);

        job.ImposedArtworkFilePath.Should().Be("imposed/frame.pdf");
        job.ImposedFromArtworkFilePath.Should().Be("artwork/source.pdf");
        job.ImposedAt.Should().Be(Now);
        job.ImposedIsManual.Should().BeFalse();
    }

    [Fact]
    public void RecordManualImposedArtwork_MarksManualWithNoSource()
    {
        var job = CreateJob();

        job.RecordManualImposedArtwork("imposed/manual.pdf", UserId, Now);

        job.ImposedArtworkFilePath.Should().Be("imposed/manual.pdf");
        job.ImposedFromArtworkFilePath.Should().BeNull();
        job.ImposedIsManual.Should().BeTrue();
    }

    [Fact]
    public void ImposedArtworkIsStale_WhenSourceArtworkChanged_ForGeneratedFrame()
    {
        var job = CreateJob();
        job.RecordImposedArtwork("imposed/frame.pdf", "artwork/v1.pdf", UserId, Now);

        job.ImposedArtworkIsStale("artwork/v1.pdf").Should().BeFalse();
        job.ImposedArtworkIsStale("artwork/v2.pdf").Should().BeTrue();
    }

    [Fact]
    public void ImposedArtworkIsStale_IsNeverTrue_ForManualUpload()
    {
        var job = CreateJob();
        job.RecordManualImposedArtwork("imposed/manual.pdf", UserId, Now);

        job.ImposedArtworkIsStale("artwork/anything.pdf").Should().BeFalse();
    }

    [Fact]
    public void RecordImposedArtwork_OverwritesAManualUpload_ClearingTheFlag()
    {
        var job = CreateJob();
        job.RecordManualImposedArtwork("imposed/manual.pdf", UserId, Now);

        job.RecordImposedArtwork("imposed/generated.pdf", "artwork/source.pdf", UserId, Now);

        job.ImposedIsManual.Should().BeFalse();
        job.ImposedArtworkFilePath.Should().Be("imposed/generated.pdf");
        job.ImposedFromArtworkFilePath.Should().Be("artwork/source.pdf");
    }

    [Fact]
    public void ClearImposedArtwork_RemovesTheReferenceButKeepsTheTemplate()
    {
        var job = CreateJob();
        job.RecordManualImposedArtwork("imposed/manual.pdf", UserId, Now);

        job.ClearImposedArtwork(UserId, Now);

        job.ImposedArtworkFilePath.Should().BeNull();
        job.ImposedFromArtworkFilePath.Should().BeNull();
        job.ImposedAt.Should().BeNull();
        job.ImposedIsManual.Should().BeFalse();
    }

    [Fact]
    public void RecordImposedArtwork_RequiresAStorageKey()
    {
        var job = CreateJob();

        var act = () => job.RecordImposedArtwork("  ", "artwork/source.pdf", UserId, Now);

        act.Should().Throw<ArgumentException>();
    }
}
