using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
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
    public Guid? SubstrateId { get; set; }

    public InkSet InkSet { get; set; } = InkSet.CMYK;

    [Range(0, 3)]
    public int WhiteHits { get; set; }

    [Range(0, 3)]
    public int SilverHits { get; set; }

    [Range(0, 100)]
    public decimal WhiteCoveragePct { get; set; } = 100m;

    [Range(0, 100)]
    public decimal SilverCoveragePct { get; set; } = 100m;

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

    public List<int?> Quantities { get; set; } = [];

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
        SubstrateId ?? Guid.Empty,
        InkSet,
        WhiteHits,
        SilverHits,
        WhiteCoveragePct / 100m,
        SilverCoveragePct / 100m,
        FinishingOperations,
        SetupWasteImpressions,
        RunningWastePct,
        LineNotes,
        Quantities.Where(q => q is > 0).Select(q => q!.Value).Distinct().OrderBy(q => q).ToList(),
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
            WhiteHits = line.WhiteHits,
            SilverHits = line.SilverHits,
            WhiteCoveragePct = line.WhiteCoveragePct * 100m,
            SilverCoveragePct = line.SilverCoveragePct * 100m,
            SetupWasteImpressions = line.SetupWasteImpressions,
            RunningWastePct = line.RunningWastePct,
            LineNotes = line.LineNotes,
            MarkupPctOverride = line.MarkupPctOverride,
            MaxLabelsAcrossOverride = line.MaxLabelsAcrossOverride,
            LabelOrientationOverride = line.LabelOrientationOverride,
            FinishingOperations = EstimateCalculationMapper
                .DeserializeFinishingOperations(line.FinishingOperationsJson).ToList(),
            Quantities = line.QuantityBreaks.OrderBy(q => q.Quantity).Select(q => (int?)q.Quantity).ToList()
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

    public Guid? ShippingMethodId { get; set; }

    [Range(0, 999999)]
    public decimal ShippingCost { get; set; }

    [StringLength(200)]
    public string? ShipToName { get; set; }

    [StringLength(200)]
    public string? ShipToStreet1 { get; set; }

    [StringLength(200)]
    public string? ShipToStreet2 { get; set; }

    [StringLength(100)]
    public string? ShipToCity { get; set; }

    [StringLength(100)]
    public string? ShipToState { get; set; }

    [StringLength(20)]
    public string? ShipToZip { get; set; }

    [StringLength(2)]
    public string? ShipToCountry { get; set; }

    public List<EstimateLinePageInput> Lines { get; set; } = [new()];

    private ShippingAddress ToShippingAddress() => new(
        ShipToName, ShipToStreet1, ShipToStreet2, ShipToCity, ShipToState, ShipToZip, ShipToCountry);

    public EstimateFormInput ToForm() => new(
        CustomerId,
        SalesRepId,
        Notes,
        ValidUntilDate,
        Lines.Select(l => l.ToForm()).ToList(),
        ShippingMethodId,
        ShippingCost,
        ToShippingAddress());

    public static EstimatePageInput FromEstimate(Domain.Entities.Estimate estimate)
    {
        return new EstimatePageInput
        {
            CustomerId = estimate.CustomerId,
            SalesRepId = estimate.SalesRepId,
            Notes = estimate.Notes,
            ValidUntilDate = estimate.ValidUntilDate,
            ShippingMethodId = estimate.ShippingMethodId,
            ShippingCost = estimate.ShippingCost,
            ShipToName = estimate.ShipToName,
            ShipToStreet1 = estimate.ShipToStreet1,
            ShipToStreet2 = estimate.ShipToStreet2,
            ShipToCity = estimate.ShipToCity,
            ShipToState = estimate.ShipToState,
            ShipToZip = estimate.ShipToZip,
            ShipToCountry = estimate.ShipToCountry,
            Lines = estimate.Lines.OrderBy(l => l.LineNumber).Select(EstimateLinePageInput.FromLine).ToList()
        };
    }
}
