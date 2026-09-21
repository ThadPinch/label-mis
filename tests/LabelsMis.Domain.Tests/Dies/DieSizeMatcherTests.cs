using LabelsMis.Domain.Dies;

namespace LabelsMis.Domain.Tests.Dies;

public class DieSizeMatcherTests
{
    [Fact]
    public void Match_SameSize_ReturnsExactUnrotated()
    {
        var match = DieSizeMatcher.Match(1.5m, 1m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m);

        match.Should().BeEquivalentTo(new { Rotated = false, DeltaAcrossIn = 0m, DeltaAroundIn = 0m, DistanceIn = 0m, IsExact = true });
    }

    [Fact]
    public void Match_SwappedDimensions_ReturnsExactRotated()
    {
        var match = DieSizeMatcher.Match(1m, 1.5m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m);

        match.Should().BeEquivalentTo(new { Rotated = true, DeltaAcrossIn = 0m, DeltaAroundIn = 0m, IsExact = true });
    }

    [Fact]
    public void Match_WithinTolerance_ReportsSignedDeltasAndDistance()
    {
        var match = DieSizeMatcher.Match(1.75m, 0.9m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m);

        match.Should().BeEquivalentTo(new { Rotated = false, DeltaAcrossIn = 0.25m, DeltaAroundIn = -0.1m, DistanceIn = 0.35m, IsExact = false });
    }

    [Theory]
    [InlineData(2.01, 1.0)]   // across just past tolerance; rotated puts around at 2.01 — still out
    [InlineData(1.5, 2.01)]   // around just past tolerance; rotated puts across at 2.01 — still out
    [InlineData(3.0, 3.0)]    // way off in both
    [InlineData(0.99, 0.49)]  // both under, and rotated 0.49 across is 1.01 short
    public void Match_OutsideToleranceInBothOrientations_ReturnsNull(decimal dieAcross, decimal dieAround)
    {
        DieSizeMatcher.Match(dieAcross, dieAround, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m)
            .Should().BeNull();
    }

    [Fact]
    public void Match_ExactlyAtToleranceBoundary_IsIncluded()
    {
        var match = DieSizeMatcher.Match(2m, 0.5m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m);

        match.Should().BeEquivalentTo(new { Rotated = false, DistanceIn = 1m });
    }

    [Fact]
    public void Match_OnlyRotatedOrientationFits_ReturnsRotated()
    {
        // As entered: across 1.0 vs 1.5 (ok), around 1.6 vs 1.0 (+0.6, out). Rotated: 1.6 vs 1.5, 1.0 vs 1.0 (in).
        var match = DieSizeMatcher.Match(1m, 1.6m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m);

        match.Should().BeEquivalentTo(new { Rotated = true, DeltaAcrossIn = 0.1m, DeltaAroundIn = 0m });
    }

    [Fact]
    public void Match_BothOrientationsFit_PrefersCloserOne()
    {
        // As entered: |1.2-1.5| + |1.4-1| = 0.7. Rotated: |1.4-1.5| + |1.2-1| = 0.3.
        var match = DieSizeMatcher.Match(1.2m, 1.4m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m);

        match.Should().BeEquivalentTo(new { Rotated = true, DistanceIn = 0.3m });
    }

    [Fact]
    public void Match_SquareDie_PrefersAsEnteredOnTie()
    {
        var match = DieSizeMatcher.Match(1.25m, 1.25m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.5m);

        match.Should().BeEquivalentTo(new { Rotated = false, DistanceIn = 0.5m });
    }

    [Fact]
    public void Match_TighterToleranceExcludesLooseMatch()
    {
        DieSizeMatcher.Match(1.75m, 1m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.125m).Should().BeNull();
        DieSizeMatcher.Match(1.75m, 1m, wantAcrossIn: 1.5m, wantAroundIn: 1m, toleranceIn: 0.25m).Should().NotBeNull();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1.5, -1)]
    public void Match_NonPositiveRequestedSize_Throws(decimal wantAcross, decimal wantAround)
    {
        var act = () => DieSizeMatcher.Match(1m, 1m, wantAcross, wantAround, 0.5m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Match_NegativeTolerance_Throws()
    {
        var act = () => DieSizeMatcher.Match(1m, 1m, 1m, 1m, -0.5m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RankComparer_OrdersExactFirstThenByClosenessThenUnrotated()
    {
        var far = new DieSizeMatch(false, 0.5m, 0.25m);            // 0.75
        var near = new DieSizeMatch(false, 0.1m, -0.05m);          // 0.15
        var nearRotated = new DieSizeMatch(true, 0.1m, 0.05m);     // 0.15, rotated
        var exactRotated = new DieSizeMatch(true, 0m, 0m);
        var exact = new DieSizeMatch(false, 0m, 0m);

        var ordered = new[] { far, nearRotated, exactRotated, near, exact }
            .OrderBy(m => m, DieSizeMatch.RankComparer)
            .ToList();

        ordered.Should().ContainInOrder(exact, exactRotated, near, nearRotated, far);
    }

    [Fact]
    public void ToleranceChoices_MatchAgreedOptionsWithHalfInchDefault()
    {
        DieSizeMatcher.ToleranceChoicesIn.Should().Equal(0.125m, 0.25m, 0.5m, 1m);
        DieSizeMatcher.ToleranceChoicesIn.Should().Contain(DieSizeMatcher.DefaultToleranceIn);
        DieSizeMatcher.DefaultToleranceIn.Should().Be(0.5m);
    }
}
