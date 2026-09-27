using LabelsMis.Web.Services.Estimates;

namespace LabelsMis.Web.Tests;

/// <summary>The finishing names the customer sees on an estimate line: run order, one per operation.</summary>
public class FinishingDescriptionResolutionTests
{
    private static readonly Guid LaminateOp = Guid.NewGuid();
    private static readonly Guid DieCutOp = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> Names = new()
    {
        [LaminateOp] = "Matte lamination",
        [DieCutOp] = "Standard die cutting"
    };

    [Fact]
    public void ResolveFinishingDescriptions_FollowsSortOrder()
    {
        var result = EstimateCalculationMapper.ResolveFinishingDescriptions(
        [
            new FinishingOperationSelectionInput(DieCutOp, null, null, 2),
            new FinishingOperationSelectionInput(LaminateOp, null, null, 1)
        ], Names);

        result.Should().Equal("Matte lamination", "Standard die cutting");
    }

    [Fact]
    public void ResolveFinishingDescriptions_ListsEachOperationOnce()
    {
        var result = EstimateCalculationMapper.ResolveFinishingDescriptions(
        [
            new FinishingOperationSelectionInput(LaminateOp, null, null, 0),
            new FinishingOperationSelectionInput(LaminateOp, null, null, 1)
        ], Names);

        result.Should().Equal("Matte lamination");
    }

    [Fact]
    public void ResolveFinishingDescriptions_SkipsBlankAndUnknownOperations()
    {
        var result = EstimateCalculationMapper.ResolveFinishingDescriptions(
        [
            new FinishingOperationSelectionInput(Guid.Empty, null, null, 0),
            new FinishingOperationSelectionInput(Guid.NewGuid(), null, null, 1),
            new FinishingOperationSelectionInput(DieCutOp, null, null, 2)
        ], Names);

        result.Should().Equal("Standard die cutting");
    }

    [Fact]
    public void ResolveFinishingDescriptions_NoSelections_IsEmpty() =>
        EstimateCalculationMapper.ResolveFinishingDescriptions([], Names).Should().BeEmpty();

    [Fact]
    public void ResolveFinishingDescriptions_RoundTripsThroughStoredJson()
    {
        var json = EstimateCalculationMapper.SerializeFinishingOperations(
        [
            new FinishingOperationSelectionInput(DieCutOp, null, null, 1, DieId: Guid.NewGuid()),
            new FinishingOperationSelectionInput(LaminateOp, 10m, 200m, 0)
        ]);

        var result = EstimateCalculationMapper.ResolveFinishingDescriptions(
            EstimateCalculationMapper.DeserializeFinishingOperations(json), Names);

        result.Should().Equal("Matte lamination", "Standard die cutting");
    }
}
