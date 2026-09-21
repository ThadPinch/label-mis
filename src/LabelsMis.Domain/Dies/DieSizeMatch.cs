namespace LabelsMis.Domain.Dies;

/// <summary>How a die's label size compares to a requested size, in whichever orientation fits best.
/// Deltas are die minus requested (positive = die is bigger), already expressed in the requested
/// across/around frame — so for a rotated match, DeltaAcrossIn is the die's around dimension minus
/// the requested across.</summary>
public record DieSizeMatch(
    bool Rotated,
    decimal DeltaAcrossIn,
    decimal DeltaAroundIn)
{
    /// <summary>Sum of absolute deltas — the closeness score; smaller is closer.</summary>
    public decimal DistanceIn => Math.Abs(DeltaAcrossIn) + Math.Abs(DeltaAroundIn);

    public bool IsExact => DeltaAcrossIn == 0m && DeltaAroundIn == 0m;

    /// <summary>Ranks exact matches first, then by closeness; an as-entered match outranks a rotated one
    /// at equal distance so the list is stable and the un-rotated option is offered first.</summary>
    public static IComparer<DieSizeMatch> RankComparer { get; } = Comparer<DieSizeMatch>.Create((a, b) =>
    {
        var exact = b.IsExact.CompareTo(a.IsExact);
        if (exact != 0) return exact;
        var distance = a.DistanceIn.CompareTo(b.DistanceIn);
        if (distance != 0) return distance;
        return a.Rotated.CompareTo(b.Rotated);
    });
}

/// <summary>Size-proximity search for dies: "I need a 1.5 × 1 — what do we have close to that?"</summary>
public static class DieSizeMatcher
{
    public static IReadOnlyList<decimal> ToleranceChoicesIn { get; } = [0.125m, 0.25m, 0.5m, 1m];

    public const decimal DefaultToleranceIn = 0.5m;

    /// <summary>Scores a die against a requested label size. Both dimensions must land within
    /// ±<paramref name="toleranceIn"/> in the same orientation; either orientation is allowed
    /// (a 1 × 1.5 die matches a 1.5 × 1 request, flagged as rotated). Returns null when the die
    /// is outside tolerance both ways. When both orientations fit, the closer one wins, and the
    /// as-entered orientation wins a tie.</summary>
    public static DieSizeMatch? Match(
        decimal dieAcrossIn,
        decimal dieAroundIn,
        decimal wantAcrossIn,
        decimal wantAroundIn,
        decimal toleranceIn)
    {
        if (wantAcrossIn <= 0 || wantAroundIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wantAcrossIn), "Requested label dimensions must be greater than zero.");
        }

        if (toleranceIn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceIn), "Tolerance cannot be negative.");
        }

        var asEntered = Score(dieAcrossIn, dieAroundIn, wantAcrossIn, wantAroundIn, toleranceIn, rotated: false);
        var rotated = Score(dieAroundIn, dieAcrossIn, wantAcrossIn, wantAroundIn, toleranceIn, rotated: true);

        if (asEntered is null) return rotated;
        if (rotated is null) return asEntered;
        return DieSizeMatch.RankComparer.Compare(asEntered, rotated) <= 0 ? asEntered : rotated;
    }

    private static DieSizeMatch? Score(
        decimal acrossIn, decimal aroundIn, decimal wantAcrossIn, decimal wantAroundIn, decimal toleranceIn, bool rotated)
    {
        var deltaAcross = acrossIn - wantAcrossIn;
        var deltaAround = aroundIn - wantAroundIn;
        if (Math.Abs(deltaAcross) > toleranceIn || Math.Abs(deltaAround) > toleranceIn)
        {
            return null;
        }

        return new DieSizeMatch(rotated, deltaAcross, deltaAround);
    }
}
