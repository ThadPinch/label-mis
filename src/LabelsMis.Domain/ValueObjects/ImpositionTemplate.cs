using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.ValueObjects;

/// <summary>
/// How a job's artwork is stepped-and-repeated onto one press frame. Seeded from the estimate's
/// layout (label size, gutters, bleed, labels across/around, orientation) but owned by the job, so
/// prepress can adjust the imposition without touching the item's spec.
///
/// The frame is <see cref="WebWidthIn"/> wide and <see cref="RepeatLengthIn"/> long; the label
/// block is centred across the web (shifted by <see cref="CrossWebOffsetIn"/>) and rows start half
/// a gutter in from the frame edge so consecutive frames tile with a full gutter between them.
/// </summary>
public record ImpositionTemplate(
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    decimal GutterAcrossIn,
    decimal GutterAroundIn,
    decimal BleedIn,
    int LabelsAcross,
    int LabelsAround,
    LabelOrientation Orientation,
    decimal WebWidthIn,
    decimal CrossWebOffsetIn,
    ImpositionMarkSide EyeMarks,
    decimal EyeMarkWidthIn,
    decimal EyeMarkHeightIn,
    bool IncludeDieLines,
    bool IncludeSlug)
{
    public const decimal DefaultEyeMarkWidthIn = 0.25m;
    public const decimal DefaultEyeMarkHeightIn = 0.125m;

    /// <summary>Builds a validated template; throws when the geometry can't produce a frame.</summary>
    public static ImpositionTemplate Create(
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        decimal gutterAcrossIn,
        decimal gutterAroundIn,
        decimal bleedIn,
        int labelsAcross,
        int labelsAround,
        LabelOrientation orientation,
        decimal webWidthIn,
        decimal crossWebOffsetIn = 0m,
        ImpositionMarkSide eyeMarks = ImpositionMarkSide.None,
        decimal? eyeMarkWidthIn = null,
        decimal? eyeMarkHeightIn = null,
        bool includeDieLines = false,
        bool includeSlug = true)
    {
        if (labelAcrossIn <= 0 || labelAroundIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelAcrossIn), "Label dimensions must be greater than zero.");
        }

        if (cornerRadiusIn < 0 || gutterAcrossIn < 0 || gutterAroundIn < 0 || bleedIn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gutterAcrossIn), "Corner radius, gutters and bleed cannot be negative.");
        }

        if (labelsAcross < 1 || labelsAround < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(labelsAcross), "Labels across and around must be at least one.");
        }

        if (webWidthIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(webWidthIn), "Web width must be greater than zero.");
        }

        var markWidth = eyeMarkWidthIn ?? DefaultEyeMarkWidthIn;
        var markHeight = eyeMarkHeightIn ?? DefaultEyeMarkHeightIn;
        if (markWidth <= 0 || markHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eyeMarkWidthIn), "Eye mark size must be greater than zero.");
        }

        return new ImpositionTemplate(
            labelAcrossIn,
            labelAroundIn,
            cornerRadiusIn,
            gutterAcrossIn,
            gutterAroundIn,
            bleedIn,
            labelsAcross,
            labelsAround,
            orientation,
            webWidthIn,
            crossWebOffsetIn,
            eyeMarks,
            markWidth,
            markHeight,
            includeDieLines,
            includeSlug);
    }

    /// <summary>The label's dimension along the web width once the orientation is applied.</summary>
    public decimal PlacedAcrossIn => Orientation == LabelOrientation.Rotated ? LabelAroundIn : LabelAcrossIn;

    /// <summary>The label's dimension along the web direction once the orientation is applied.</summary>
    public decimal PlacedAroundIn => Orientation == LabelOrientation.Rotated ? LabelAcrossIn : LabelAroundIn;

    public decimal PitchAcrossIn => PlacedAcrossIn + GutterAcrossIn;
    public decimal PitchAroundIn => PlacedAroundIn + GutterAroundIn;

    /// <summary>Width of the label block: N labels plus the N−1 gutters between them.</summary>
    public decimal BlockWidthIn => LabelsAcross * PlacedAcrossIn + (LabelsAcross - 1) * GutterAcrossIn;

    /// <summary>Frame length — one full pitch per row so frames tile continuously down the web.</summary>
    public decimal RepeatLengthIn => LabelsAround * PitchAroundIn;

    public int LabelsPerFrame => LabelsAcross * LabelsAround;

    /// <summary>Unprinted margin left of the block (right margin mirrors it, less the offset).</summary>
    public decimal LeftMarginIn => (WebWidthIn - BlockWidthIn) / 2m + CrossWebOffsetIn;
    public decimal RightMarginIn => (WebWidthIn - BlockWidthIn) / 2m - CrossWebOffsetIn;

    /// <summary>The label block spills past the web edge — the frame can't be run as is.</summary>
    public bool OverflowsWeb => LeftMarginIn < 0 || RightMarginIn < 0;
}
