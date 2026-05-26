using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Estimates;
using System.ComponentModel.DataAnnotations;

namespace LabelsMis.Web.Pages.Estimates;

public class EstimateLinePageInput
{
    public Guid? Id { get; set; }

    public Guid? SourceProductId { get; set; }

    [Required, StringLength(500)]
    public string ProductDescription { get; set; } = string.Empty;

    [Range(0.0001, 100)]
    public decimal LabelAcrossIn { get; set; } = 4.0m;

    [Range(0.0001, 100)]
    public decimal LabelAroundIn { get; set; } = 3.0m;

    [Range(0, 10)]
    public decimal CornerRadiusIn { get; set; } = 0.125m;

    [Range(0, 1)]
    public decimal GutterAcrossIn { get; set; } = 0.0625m;

    [Range(0, 1)]
    public decimal GutterAroundIn { get; set; } = 0.0625m;

    [Range(0, 1)]
    public decimal BleedIn { get; set; } = 0.0625m;

    [Required]
    public Guid SubstrateId { get; set; }

    public InkSet InkSet { get; set; } = InkSet.CMYK;

    public bool WhiteInkUsed { get; set; }

    [Range(0, 10000)]
    public decimal SetupWasteImpressions { get; set; } = 30m;

    [Range(0, 1)]
    public decimal RunningWastePct { get; set; } = 0.03m;

    [StringLength(2000)]
    public string? LineNotes { get; set; }

    [Range(0, 10)]
    public decimal? MarkupPctOverride { get; set; }

    [Range(1, 100)]
    public int? MaxLabelsAcrossOverride { get; set; }

    public LabelOrientation? LabelOrientationOverride { get; set; }

    public List<FinishingOperationSelectionInput> FinishingOperations { get; set; } = [];

    public List<int> Quantities { get; set; } = [5000, 10000, 25000];

    public EstimateLineFormInput ToForm() => new(
        Id,
        SourceProductId,
        ProductDescription,
        LabelAcrossIn,
        LabelAroundIn,
        CornerRadiusIn,
        GutterAcrossIn,
        GutterAroundIn,
        BleedIn,
        SubstrateId,
        InkSet,
        WhiteInkUsed,
        FinishingOperations,
        SetupWasteImpressions,
        RunningWastePct,
        LineNotes,
        Quantities.Where(q => q > 0).ToList(),
        MarkupPctOverride,
        MaxLabelsAcrossOverride,
        LabelOrientationOverride);

    public static EstimateLinePageInput FromLine(Domain.Entities.EstimateLine line)
    {
        return new EstimateLinePageInput
        {
            Id = line.Id,
            SourceProductId = line.SourceProductId,
            ProductDescription = line.ProductDescription,
            LabelAcrossIn = line.LabelAcrossIn,
            LabelAroundIn = line.LabelAroundIn,
            CornerRadiusIn = line.CornerRadiusIn,
            GutterAcrossIn = line.GutterAcrossIn,
            GutterAroundIn = line.GutterAroundIn,
            BleedIn = line.BleedIn,
            SubstrateId = line.SubstrateId,
            InkSet = line.InkSet,
            WhiteInkUsed = line.WhiteInkUsed,
            SetupWasteImpressions = line.SetupWasteImpressions,
            RunningWastePct = line.RunningWastePct,
            LineNotes = line.LineNotes,
            MarkupPctOverride = line.MarkupPctOverride,
            MaxLabelsAcrossOverride = line.MaxLabelsAcrossOverride,
            LabelOrientationOverride = line.LabelOrientationOverride,
            FinishingOperations = EstimateCalculationMapper
                .DeserializeFinishingOperations(line.FinishingOperationsJson).ToList(),
            Quantities = line.QuantityBreaks.OrderBy(q => q.Quantity).Select(q => q.Quantity).ToList()
        };
    }
}

public class EstimatePageInput
{
    [Required]
    public Guid CustomerId { get; set; }

    public Guid? SalesRepId { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? ValidUntilDate { get; set; }

    public List<EstimateLinePageInput> Lines { get; set; } = [new()];

    public EstimateFormInput ToForm() => new(
        CustomerId,
        SalesRepId,
        Notes,
        ValidUntilDate,
        Lines.Select(l => l.ToForm()).ToList());

    public static EstimatePageInput FromEstimate(Domain.Entities.Estimate estimate)
    {
        return new EstimatePageInput
        {
            CustomerId = estimate.CustomerId,
            SalesRepId = estimate.SalesRepId,
            Notes = estimate.Notes,
            ValidUntilDate = estimate.ValidUntilDate,
            Lines = estimate.Lines.OrderBy(l => l.LineNumber).Select(EstimateLinePageInput.FromLine).ToList()
        };
    }
}
