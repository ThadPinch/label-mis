using LabelsMis.Web.Services.Estimates;

namespace LabelsMis.Web.Tests;

/// <summary>Which die a finishing list runs on: the die named on its die-cut row, nothing else.</summary>
public class FinishingDieResolutionTests
{
    private static readonly Guid LaminateOp = Guid.NewGuid();
    private static readonly Guid DieCutOp = Guid.NewGuid();

    [Fact]
    public void ResolveDieId_ReturnsTheDieOnTheDieCutRow()
    {
        var die = Guid.NewGuid();
        var json = EstimateCalculationMapper.SerializeFinishingOperations(
        [
            new FinishingOperationSelectionInput(LaminateOp, null, null, 0),
            new FinishingOperationSelectionInput(DieCutOp, null, null, 1, DieId: die)
        ]);

        EstimateCalculationMapper.ResolveDieId(json).Should().Be(die);
    }

    [Fact]
    public void ResolveDieId_FollowsSortOrderWhenTwoRowsNameDies()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var json = EstimateCalculationMapper.SerializeFinishingOperations(
        [
            new FinishingOperationSelectionInput(Guid.NewGuid(), null, null, 5, DieId: second),
            new FinishingOperationSelectionInput(DieCutOp, null, null, 2, DieId: first)
        ]);

        EstimateCalculationMapper.ResolveDieId(json).Should().Be(first);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    public void ResolveDieId_NoRows_IsNull(string? json) =>
        EstimateCalculationMapper.ResolveDieId(json).Should().BeNull();

    [Fact]
    public void ResolveDieId_IgnoresRowsWithoutADie()
    {
        var json = EstimateCalculationMapper.SerializeFinishingOperations(
        [
            new FinishingOperationSelectionInput(LaminateOp, 10m, 200m, 0),
            new FinishingOperationSelectionInput(DieCutOp, null, null, 1, DieId: Guid.Empty)
        ]);

        EstimateCalculationMapper.ResolveDieId(json).Should().BeNull();
    }
}
