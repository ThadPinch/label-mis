using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Tests.Entities;

public class EstimateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void MarkWon_ThenEnsureCanTransitionToDraft_Throws()
    {
        var estimate = CreateDraftWithLine();
        estimate.MarkSent("/tmp/test.pdf", UserId, Now);
        estimate.MarkWon(UserId, Now);

        var act = () => estimate.EnsureCanTransitionToDraft();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot return to draft*");
    }

    [Fact]
    public void Cancel_WhenWon_SetsCancelled()
    {
        var estimate = CreateDraftWithLine();
        estimate.MarkSent("/tmp/test.pdf", UserId, Now);
        estimate.MarkWon(UserId, Now);

        estimate.Cancel(UserId, Now);

        estimate.Status.Should().Be(EstimateStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_Throws()
    {
        var estimate = CreateDraftWithLine();
        estimate.Cancel(UserId, Now);

        var act = () => estimate.Cancel(UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already cancelled*");
    }

    [Fact]
    public void MarkLost_WhenCancelled_Throws()
    {
        var estimate = CreateDraftWithLine();
        estimate.Cancel(UserId, Now);

        var act = () => estimate.MarkLost("reason", UserId, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BeginRevision_AfterSent_ResetsToDraftAndIncrementsRevision()
    {
        var estimate = CreateDraftWithLine();
        estimate.MarkSent("/tmp/test.pdf", UserId, Now);

        var revision = estimate.BeginRevision(Guid.NewGuid(), "{}", UserId, Now);

        revision.RevisionNumber.Should().Be(1);
        estimate.RevisionNumber.Should().Be(2);
        estimate.Status.Should().Be(EstimateStatus.Draft);
        estimate.PdfFilePath.Should().BeNull();
    }

    [Fact]
    public void UpdateDraft_WhenNotDraft_Throws()
    {
        var estimate = CreateDraftWithLine();
        estimate.MarkSent("/tmp/test.pdf", UserId, Now);

        var act = () => estimate.UpdateDraft(null, "notes", null, null, null, 0m, ShippingAddress.Empty, UserId, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureCanDelete_WhenDraft_DoesNotThrow()
    {
        var estimate = CreateDraftWithLine();

        var act = () => estimate.EnsureCanDelete();

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanDelete_WhenSent_Throws()
    {
        var estimate = CreateDraftWithLine();
        estimate.MarkSent("/tmp/test.pdf", UserId, Now);

        var act = () => estimate.EnsureCanDelete();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only draft estimates can be deleted*");
    }

    [Fact]
    public void MarkSent_WithNoLines_Throws()
    {
        var estimate = Estimate.CreateDraft(
            Guid.NewGuid(), "EST-2026-00099", Guid.NewGuid(), null,
            null, null, null, null, 0m, ShippingAddress.Empty, UserId, Now);

        var act = () => estimate.MarkSent(null, UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one line*");
    }

    private static Estimate CreateDraftWithLine()
    {
        var estimate = Estimate.CreateDraft(
            Guid.NewGuid(), "EST-2026-00001", Guid.NewGuid(), null,
            null, null, null, null, 0m, ShippingAddress.Empty, UserId, Now);
        var line = EstimateLine.Create(
            Guid.NewGuid(), estimate.Id, 1, null, "Test labels",
            4, 3, 0.125m, 0.0625m, 0.0625m, 0.0625m,
            Guid.NewGuid(), InkSet.CMYK, 0, 1m, "[]", "[]", 30, 0.03m, null, null, null, null, null, null,
            UserId, Now);
        estimate.AddLine(line);
        return estimate;
    }
}
