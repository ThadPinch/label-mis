using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Web.Services.Estimates;
using System.ComponentModel.DataAnnotations;

namespace LabelsMis.Web.Pages.Estimates;

public class SpotSelectionPageInput
{
    public Guid InkId { get; set; }

    [Range(0, 3)]
    public int Hits { get; set; }

    [Range(0, 100)]
    public decimal CoveragePct { get; set; } = 100m;

    public int SortOrder { get; set; }
}

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

    [Range(0, 100)]
    public decimal WhiteCoveragePct { get; set; } = 100m;

    public List<SpotSelectionPageInput> Spots { get; set; } = [];

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

    public UnwindDirection? Unwind { get; set; }

    [Range(0.0001, 1000)]
    public decimal? ShrinkLayflatIn { get; set; }

    public List<FinishingOperationSelectionInput> FinishingOperations { get; set; } = [];

    public List<int?> Quantities { get; set; } = [];

    /// <summary>Per-quantity markup overrides as a JSON object keyed by quantity
    /// (e.g. {"1000":0.5}). Round-tripped through a hidden field so the form and the
    /// calculate endpoint share one shape.</summary>
    public string? QuantityMarkupOverridesJson { get; set; }

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
        WhiteCoveragePct / 100m,
        Spots.Select((s, idx) => new SpotSelectionInput(s.InkId, s.Hits, s.CoveragePct / 100m, idx)).ToList(),
        FinishingOperations,
        SetupWasteImpressions,
        RunningWastePct,
        LineNotes,
        Quantities.Where(q => q is > 0).Select(q => q!.Value).Distinct().OrderBy(q => q).ToList(),
        MarkupPctOverride,
        MaxLabelsAcrossOverride,
        LabelOrientationOverride,
        ParseQuantityMarkupOverrides(QuantityMarkupOverridesJson),
        Unwind,
        ShrinkLayflatIn);

    private static IReadOnlyDictionary<int, decimal>? ParseQuantityMarkupOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, decimal>>(json);
            return parsed is { Count: > 0 } ? parsed : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

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
            WhiteCoveragePct = line.WhiteCoveragePct * 100m,
            Spots = EstimateCalculationMapper.DeserializeSpots(line.SpotsJson)
                .OrderBy(s => s.SortOrder)
                .Select(s => new SpotSelectionPageInput
                {
                    InkId = s.InkId,
                    Hits = s.Hits,
                    CoveragePct = s.CoveragePct * 100m,
                    SortOrder = s.SortOrder
                }).ToList(),
            SetupWasteImpressions = line.SetupWasteImpressions,
            RunningWastePct = line.RunningWastePct,
            LineNotes = line.LineNotes,
            ShrinkLayflatIn = line.ShrinkLayflatIn,
            MarkupPctOverride = line.MarkupPctOverride,
            MaxLabelsAcrossOverride = line.MaxLabelsAcrossOverride,
            LabelOrientationOverride = line.LabelOrientationOverride,
            Unwind = line.Unwind,
            FinishingOperations = EstimateCalculationMapper
                .DeserializeFinishingOperations(line.FinishingOperationsJson).ToList(),
            Quantities = line.QuantityBreaks.OrderBy(q => q.Quantity).Select(q => (int?)q.Quantity).ToList(),
            QuantityMarkupOverridesJson = SerializeQuantityMarkupOverrides(line)
        };
    }

    private static string? SerializeQuantityMarkupOverrides(Domain.Entities.EstimateLine line)
    {
        var overrides = line.QuantityBreaks
            .Where(q => q.MarkupPctOverride.HasValue)
            .ToDictionary(q => q.Quantity, q => q.MarkupPctOverride!.Value);
        return overrides.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(overrides);
    }
}

/// <summary>A flat, non-label charge row (die creation, design time) on the estimate form.</summary>
public class EstimateChargePageInput
{
    public Guid? Id { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(1, 1000000)]
    public int Quantity { get; set; } = 1;

    [Range(0, 1000000)]
    public decimal UnitPrice { get; set; }

    public EstimateChargeFormInput ToForm() => new(Id, Description ?? string.Empty, Quantity, UnitPrice);

    public static EstimateChargePageInput FromCharge(Domain.Entities.EstimateCharge charge) => new()
    {
        Id = charge.Id,
        Description = charge.Description,
        Quantity = charge.Quantity,
        UnitPrice = charge.UnitPrice
    };
}

public class EstimatePageInput
{
    [Required]
    public Guid CustomerId { get; set; }

    public Guid? SalesRepId { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    [StringLength(4000)]
    public string? BillingNotes { get; set; }

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

    public List<EstimateChargePageInput> Charges { get; set; } = [];

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
        ToShippingAddress(),
        BillingNotes,
        Charges.Where(c => !string.IsNullOrWhiteSpace(c.Description)).Select(c => c.ToForm()).ToList());

    public static EstimatePageInput FromEstimate(Domain.Entities.Estimate estimate)
    {
        return new EstimatePageInput
        {
            CustomerId = estimate.CustomerId,
            SalesRepId = estimate.SalesRepId,
            Notes = estimate.Notes,
            BillingNotes = estimate.BillingNotes,
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
            Lines = estimate.Lines.OrderBy(l => l.LineNumber).Select(EstimateLinePageInput.FromLine).ToList(),
            Charges = estimate.Charges.OrderBy(c => c.LineNumber).Select(EstimateChargePageInput.FromCharge).ToList()
        };
    }
}
