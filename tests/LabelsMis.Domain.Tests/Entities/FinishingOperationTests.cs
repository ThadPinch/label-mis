using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class FinishingOperationTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Create_DieCut_NeedsNoDie()
    {
        // A die-cut task is generic: the die is chosen per product and per estimate/order line.
        var operation = FinishingOperation.Create(
            Guid.NewGuid(),
            " die-rot ",
            "  Rotary die-cut ",
            FinishingOperationType.DieCut,
            30m,
            250m,
            "Die cutter",
            110m,
            UserId,
            Now);

        operation.Code.Should().Be("DIE-ROT");
        operation.Description.Should().Be("Rotary die-cut");
        operation.OperationType.Should().Be(FinishingOperationType.DieCut);
        operation.DefaultSetupMinutes.Should().Be(30m);
        operation.DefaultRunSpeedFpm.Should().Be(250m);
        operation.EquipmentName.Should().Be("Die cutter");
        operation.CostPerHour.Should().Be(110m);
    }

    [Theory]
    [InlineData("", "Gloss laminate", "Laminator", "code")]
    [InlineData("LAM", "", "Laminator", "description")]
    [InlineData("LAM", "Gloss laminate", " ", "equipmentName")]
    public void Create_MissingRequiredText_Throws(string code, string description, string equipment, string parameter)
    {
        var act = () => FinishingOperation.Create(
            Guid.NewGuid(), code, description, FinishingOperationType.Laminate, 15m, 200m, equipment, 90m, UserId, Now);

        act.Should().Throw<ArgumentException>().WithParameterName(parameter);
    }

    [Fact]
    public void Create_ZeroRunSpeed_Throws()
    {
        var act = () => FinishingOperation.Create(
            Guid.NewGuid(), "LAM", "Gloss laminate", FinishingOperationType.Laminate, 15m, 0m, "Laminator", 90m, UserId, Now);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("defaultRunSpeedFpm");
    }

    [Fact]
    public void Update_ReplacesEveryField()
    {
        var operation = FinishingOperation.Create(
            Guid.NewGuid(), "DIE-ROT", "Rotary die-cut", FinishingOperationType.DieCut, 30m, 250m, "Die cutter", 110m, UserId, Now);

        var later = Now.AddMinutes(5);
        operation.Update("lam-gloss", "Gloss laminate", FinishingOperationType.Laminate, 15m, 200m, "Laminator", 90m, UserId, later);

        operation.Code.Should().Be("LAM-GLOSS");
        operation.Description.Should().Be("Gloss laminate");
        operation.OperationType.Should().Be(FinishingOperationType.Laminate);
        operation.DefaultSetupMinutes.Should().Be(15m);
        operation.DefaultRunSpeedFpm.Should().Be(200m);
        operation.EquipmentName.Should().Be("Laminator");
        operation.CostPerHour.Should().Be(90m);
        operation.ModifiedAt.Should().Be(later);
    }
}
