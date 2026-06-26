using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class EstimateLine : EntityBase
{
    private readonly List<EstimateQuantityBreak> _quantityBreaks = [];

    private EstimateLine()
    {
    }

    public Guid EstimateId { get; private set; }
    public Estimate Estimate { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public Guid? SourceProductId { get; private set; }
    public string ProductDescription { get; private set; } = string.Empty;
    public decimal LabelAcrossIn { get; private set; }
    public decimal LabelAroundIn { get; private set; }
    public decimal CornerRadiusIn { get; private set; }
    public decimal GutterAcrossIn { get; private set; }
    public decimal GutterAroundIn { get; private set; }
    public decimal BleedIn { get; private set; }
    public Guid SubstrateId { get; private set; }
    public Stock Substrate { get; private set; } = null!;
    public InkSet InkSet { get; private set; }
    public int WhiteHits { get; private set; }
    public decimal WhiteCoveragePct { get; private set; } = 1m;
    public string SpotsJson { get; private set; } = "[]";
    public string FinishingOperationsJson { get; private set; } = "[]";
    public decimal SetupWasteImpressions { get; private set; }
    public decimal RunningWastePct { get; private set; }
    public string? LineNotes { get; private set; }
    public decimal? MarkupPctOverride { get; private set; }
    public int? MaxLabelsAcrossOverride { get; private set; }
    public LabelOrientation? LabelOrientationOverride { get; private set; }

    public IReadOnlyCollection<EstimateQuantityBreak> QuantityBreaks => _quantityBreaks;

    public static EstimateLine Create(
        Guid id,
        Guid estimateId,
        int lineNumber,
        Guid? sourceProductId,
        string productDescription,
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        decimal gutterAcrossIn,
        decimal gutterAroundIn,
        decimal bleedIn,
        Guid substrateId,
        InkSet inkSet,
        int whiteHits,
        decimal whiteCoveragePct,
        string spotsJson,
        string finishingOperationsJson,
        decimal setupWasteImpressions,
        decimal runningWastePct,
        string? lineNotes,
        decimal? markupPctOverride,
        int? maxLabelsAcrossOverride,
        LabelOrientation? labelOrientationOverride,
        Guid createdById,
        DateTime createdAt)
    {
        ValidateDimensions(labelAcrossIn, labelAroundIn);
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line number must be greater than zero.");
        }

        var line = new EstimateLine
        {
            EstimateId = estimateId,
            LineNumber = lineNumber,
            SourceProductId = sourceProductId,
            ProductDescription = productDescription.Trim(),
            LabelAcrossIn = labelAcrossIn,
            LabelAroundIn = labelAroundIn,
            CornerRadiusIn = cornerRadiusIn,
            GutterAcrossIn = gutterAcrossIn,
            GutterAroundIn = gutterAroundIn,
            BleedIn = bleedIn,
            SubstrateId = substrateId,
            InkSet = inkSet,
            WhiteHits = Math.Max(0, whiteHits),
            WhiteCoveragePct = whiteCoveragePct <= 0 ? 1m : whiteCoveragePct,
            SpotsJson = string.IsNullOrWhiteSpace(spotsJson) ? "[]" : spotsJson,
            FinishingOperationsJson = string.IsNullOrWhiteSpace(finishingOperationsJson) ? "[]" : finishingOperationsJson,
            SetupWasteImpressions = setupWasteImpressions,
            RunningWastePct = runningWastePct,
            LineNotes = string.IsNullOrWhiteSpace(lineNotes) ? null : lineNotes.Trim(),
            MarkupPctOverride = markupPctOverride,
            MaxLabelsAcrossOverride = maxLabelsAcrossOverride,
            LabelOrientationOverride = labelOrientationOverride
        };
        line.SetCreated(id, createdById, createdAt);
        return line;
    }

    public void Update(
        int lineNumber,
        Guid? sourceProductId,
        string productDescription,
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        decimal gutterAcrossIn,
        decimal gutterAroundIn,
        decimal bleedIn,
        Guid substrateId,
        InkSet inkSet,
        int whiteHits,
        decimal whiteCoveragePct,
        string spotsJson,
        string finishingOperationsJson,
        decimal setupWasteImpressions,
        decimal runningWastePct,
        string? lineNotes,
        decimal? markupPctOverride,
        int? maxLabelsAcrossOverride,
        LabelOrientation? labelOrientationOverride,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        ValidateDimensions(labelAcrossIn, labelAroundIn);
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line number must be greater than zero.");
        }

        LineNumber = lineNumber;
        SourceProductId = sourceProductId;
        ProductDescription = productDescription.Trim();
        LabelAcrossIn = labelAcrossIn;
        LabelAroundIn = labelAroundIn;
        CornerRadiusIn = cornerRadiusIn;
        GutterAcrossIn = gutterAcrossIn;
        GutterAroundIn = gutterAroundIn;
        BleedIn = bleedIn;
        SubstrateId = substrateId;
        InkSet = inkSet;
        WhiteHits = Math.Max(0, whiteHits);
        WhiteCoveragePct = whiteCoveragePct <= 0 ? 1m : whiteCoveragePct;
        SpotsJson = string.IsNullOrWhiteSpace(spotsJson) ? "[]" : spotsJson;
        FinishingOperationsJson = string.IsNullOrWhiteSpace(finishingOperationsJson) ? "[]" : finishingOperationsJson;
        SetupWasteImpressions = setupWasteImpressions;
        RunningWastePct = runningWastePct;
        LineNotes = string.IsNullOrWhiteSpace(lineNotes) ? null : lineNotes.Trim();
        MarkupPctOverride = markupPctOverride;
        MaxLabelsAcrossOverride = maxLabelsAcrossOverride;
        LabelOrientationOverride = labelOrientationOverride;
        SetModified(modifiedById, modifiedAt);
    }

    public void ReplaceQuantityBreaks(IEnumerable<EstimateQuantityBreak> breaks)
    {
        _quantityBreaks.Clear();
        _quantityBreaks.AddRange(breaks);
    }

    public void AddQuantityBreak(EstimateQuantityBreak quantityBreak) => _quantityBreaks.Add(quantityBreak);

    private static void ValidateDimensions(decimal labelAcrossIn, decimal labelAroundIn)
    {
        if (labelAcrossIn <= 0 || labelAroundIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(labelAcrossIn), "Label dimensions must be greater than zero.");
        }
    }
}
