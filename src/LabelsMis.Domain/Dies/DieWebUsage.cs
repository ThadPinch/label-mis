namespace LabelsMis.Domain.Dies;

/// <summary>
/// How much of the die's web width its labels use: labels across × label width ÷ web width.
/// A low figure usually means the die was entered sideways (across and around swapped), which
/// over-costs every estimate on it — two 12 oz dies were stored 8" across, 1-up on a 13" web.
/// </summary>
public static class DieWebUsage
{
    /// <summary>Below this share of the web width the die form warns (Andrew's 60% rule).</summary>
    public const decimal LowUsageThreshold = 0.60m;

    /// <summary>Share of the web the labels cover (0–1+), or null when there's nothing to judge.</summary>
    public static decimal? Usage(decimal labelAcrossIn, int labelsAcross, decimal webWidthIn) =>
        webWidthIn > 0 && labelAcrossIn > 0 && labelsAcross > 0
            ? labelAcrossIn * labelsAcross / webWidthIn
            : null;

    /// <summary>The warning to show, or null when the labels use at least 60% of the web.</summary>
    public static string? Warning(decimal labelAcrossIn, int labelsAcross, decimal webWidthIn)
    {
        var usage = Usage(labelAcrossIn, labelsAcross, webWidthIn);
        if (usage is null || usage >= LowUsageThreshold)
        {
            return null;
        }

        return $"{labelsAcross} across × {labelAcrossIn:0.###}\" = {labelAcrossIn * labelsAcross:0.###}\" uses only " +
               $"{usage:P0} of the {webWidthIn:0.###}\" web. Check the die isn't entered sideways (across and around swapped).";
    }
}
