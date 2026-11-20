using FluentAssertions;
using FrontEndSuite.PdfPlatform.Fonts;
using LabelsMis.Web.Pdf;

namespace LabelsMis.Web.Tests;

public class JobTicketNotesTests
{
    [Fact]
    public void Build_WhenJobNoteCopiesLineNote_PrintsItOnce()
    {
        var notes = JobTicketNotes.Build("White BOPP/Vinyl\n27 versions, 5 of each", "white bopp/vinyl  27 versions, 5 of each ", 135);

        notes.LineText.Should().Be("White BOPP/Vinyl\n27 versions, 5 of each");
        notes.JobText.Should().BeNull();
        notes.Versions.Should().BeEmpty();
    }

    [Fact]
    public void Build_WhenJobNoteDiffers_KeepsBoth()
    {
        var notes = JobTicketNotes.Build("Unwind #4, new die", "Unwind #3, customer-supplied die", 500);

        notes.LineText.Should().Be("Unwind #4, new die");
        notes.JobText.Should().Be("Unwind #3, customer-supplied die");
    }

    [Fact]
    public void Build_WhenNoNotes_IsEmpty()
    {
        JobTicketNotes.Build(null, "  ", 100).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Build_WhenOnlyJobNoteHasVersions_ReadsThemFromIt()
    {
        var notes = JobTicketNotes.Build(null, "VERSIONS: A 60 | B 40", 100);

        notes.Versions.Should().HaveCount(2);
        notes.JobText.Should().BeNull();
    }

    [Fact]
    public void SplitVersions_WhenQuantitiesAddUp_ReturnsChecklistAndTheRest()
    {
        const string text = "VERSIONS: Ducks Unlimited 420 | North Ward Trading Post opt 1 100 | Purple Galaxy 1,980\nFINISH: satin, not gloss";

        var versions = JobTicketNotes.SplitVersions(text, 2500, out var rest);

        versions.Should().Equal(
            new TicketVersion("Ducks Unlimited", 420),
            new TicketVersion("North Ward Trading Post opt 1", 100),
            new TicketVersion("Purple Galaxy", 1980));
        rest.Should().Be("FINISH: satin, not gloss");
    }

    [Theory]
    [InlineData("VERSIONS: Batch 12 | Batch 13", 500)] // names ending in a number, not quantities
    [InlineData("VERSIONS: Mild 300 | Hot", 300)] // an entry without a quantity
    [InlineData("VERSIONS: Mild 300 | Hot 100", 500)] // does not add up to the job
    [InlineData("VERSIONS: A 100\nVERSIONS: B 100", 200)] // two lists: ambiguous
    [InlineData("Koffee Kolsch 12oz Gloss - 10,114", 10114)] // no keyword
    public void SplitVersions_WhenNotACleanList_LeavesTheTextAlone(string text, int quantityOrdered)
    {
        var versions = JobTicketNotes.SplitVersions(text, quantityOrdered, out var rest);

        versions.Should().BeEmpty();
        rest.Should().Be(text);
    }

    [Fact]
    public void CapLines_WhenNoteIsLong_SplitsAfterTheCapAndKeepsEveryWord()
    {
        var text = string.Join("\n", Enumerable.Range(1, 14).Select(i => $"Line {i}"));

        var (shown, rest) = JobTicketV2PdfGenerator.CapLines(text, StandardFont.Helvetica, 9f, 300f, 10);

        shown.Split('\n').Should().HaveCount(10);
        rest.Should().Be("Line 11\nLine 12\nLine 13\nLine 14");
    }

    [Fact]
    public void CapLines_WhenCutLandsInsideAParagraph_RejoinsItsRemainder()
    {
        var paragraph = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"word{i}"));

        var (shown, rest) = JobTicketV2PdfGenerator.CapLines(paragraph, StandardFont.Helvetica, 9f, 120f, 3);

        shown.Split('\n').Should().HaveCount(3);
        rest.Should().NotContain("\n");
        (shown.Replace('\n', ' ') + " " + rest).Should().Be(paragraph);
    }

    [Fact]
    public void CapLines_WhenNoteFits_HasNoRemainder()
    {
        var (shown, rest) = JobTicketV2PdfGenerator.CapLines("Short note.", StandardFont.Helvetica, 9f, 300f, 10);

        shown.Should().Be("Short note.");
        rest.Should().BeNull();
    }

    [Theory]
    [InlineData(null, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(1, 2, 1)]
    [InlineData(null, 2, 2)]
    [InlineData(7, 1, 1)]
    [InlineData(null, 9, 1)]
    public void ResolveTicketVersion_PicksRequestedElseDefault(int? requested, int configuredDefault, int expected)
    {
        new JobOptions { DefaultTicketVersion = configuredDefault }.ResolveTicketVersion(requested).Should().Be(expected);
    }
}
