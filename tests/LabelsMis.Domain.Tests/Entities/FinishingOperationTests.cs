using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class FinishingOperationTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly Guid DieId = Guid.NewGuid();

    [Fact]
    public void Create_DieCutWithoutDie_Throws()
    {
        var act = () => FinishingOperation.Create(
            Guid.NewGuid(),
            "DIE-ROT",
            "Rotary die-cut",
            FinishingOperationType.DieCut,
            dieId: null,
            30m,
            250m,
            "Die cutter",
            110m,
            UserId,
            Now);

        act.Should().Throw<ArgumentException>().WithParameterName("dieId");
    }

    [Fact]
    public void Create_LaminateWithDie_IgnoresDieId()
    {
        var operation = FinishingOperation.Create(
            Guid.NewGuid(),
            "LAM-GLOSS",
            "Gloss laminate",
            FinishingOperationType.Laminate,
            dieId: DieId,
            15m,
            200m,
            "Laminator",
            90m,
            UserId,
            Now);

        operation.DieId.Should().BeNull();
    }

    [Fact]
    public void Create_DieCutWithDie_StoresDieId()
    {
        var operation = FinishingOperation.Create(
            Guid.NewGuid(),
            "DIE-ROT",
            "Rotary die-cut",
            FinishingOperationType.DieCut,
            DieId,
            30m,
            250m,
            "Die cutter",
            110m,
            UserId,
            Now);

        operation.DieId.Should().Be(DieId);
    }

    [Fact]
    public void Update_ChangeFromDieCutToLaminate_ClearsDieId()
    {
        var operation = FinishingOperation.Create(
            Guid.NewGuid(),
            "DIE-ROT",
            "Rotary die-cut",
            FinishingOperationType.DieCut,
            DieId,
            30m,
            250m,
            "Die cutter",
            110m,
            UserId,
            Now);

        operation.Update(
            "LAM-GLOSS",
            "Gloss laminate",
            FinishingOperationType.Laminate,
            DieId,
            15m,
            200m,
            "Laminator",
            90m,
            UserId,
            Now);

        operation.OperationType.Should().Be(FinishingOperationType.Laminate);
        operation.DieId.Should().BeNull();
    }
}
