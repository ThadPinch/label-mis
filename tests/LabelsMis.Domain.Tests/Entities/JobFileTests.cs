using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class JobFileTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Create_TrimsAndDefaultsContentType()
    {
        var file = JobFile.Create(
            Guid.NewGuid(), Guid.NewGuid(), JobFileCategory.PrintFile,
            "  job-files/x/a.pdf  ", "  label.pdf ", "", 12, "  rev 2 ", UserId, Now);

        file.FileKey.Should().Be("job-files/x/a.pdf");
        file.OriginalFileName.Should().Be("label.pdf");
        file.ContentType.Should().Be("application/octet-stream");
        file.Notes.Should().Be("rev 2");
        file.IsArchived.Should().BeFalse();
        file.CreatedById.Should().Be(UserId);
    }

    [Theory]
    [InlineData("", "a.pdf", 1)]
    [InlineData("key", " ", 1)]
    [InlineData("key", "a.pdf", 0)]
    public void Create_WithMissingKeyNameOrSize_Throws(string key, string name, long size)
    {
        var act = () => JobFile.Create(
            Guid.NewGuid(), Guid.NewGuid(), JobFileCategory.Other, key, name, "application/pdf", size, null, UserId, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Archive_ThenRestore_RoundTrips()
    {
        var file = Create();
        var archiver = Guid.NewGuid();
        var later = Now.AddMinutes(5);

        file.Archive(archiver, later);

        file.IsArchived.Should().BeTrue();
        file.ArchivedAt.Should().Be(later);
        file.ArchivedById.Should().Be(archiver);
        file.ModifiedById.Should().Be(archiver);

        file.Restore(UserId, later.AddMinutes(1));

        file.IsArchived.Should().BeFalse();
        file.ArchivedAt.Should().BeNull();
        file.ArchivedById.Should().BeNull();
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_KeepsFirstStamp()
    {
        var file = Create();
        var first = Now.AddMinutes(1);

        file.Archive(UserId, first);
        file.Archive(Guid.NewGuid(), first.AddHours(1));

        file.ArchivedAt.Should().Be(first);
        file.ArchivedById.Should().Be(UserId);
    }

    [Fact]
    public void Recategorize_ChangesCategoryAndStampsModified()
    {
        var file = Create();

        file.Recategorize(JobFileCategory.ApprovedArtwork, UserId, Now.AddMinutes(1));

        file.Category.Should().Be(JobFileCategory.ApprovedArtwork);
        file.ModifiedAt.Should().Be(Now.AddMinutes(1));
    }

    private static JobFile Create() => JobFile.Create(
        Guid.NewGuid(), Guid.NewGuid(), JobFileCategory.CustomerArtwork,
        "job-files/j/f.pdf", "f.pdf", "application/pdf", 100, null, UserId, Now);
}
