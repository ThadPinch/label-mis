using System.Text.Json;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating.Models;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Estimates;

public static class EstimateInkSetMapper
{
    public static IndigoInkSet ToIndigoInkSet(InkSet inkSet) =>
        Enum.Parse<IndigoInkSet>(inkSet.ToString());
}

public record FinishingOperationSelectionInput(
    Guid OperationId,
    decimal? SetupMinutesOverride,
    decimal? RunSpeedFpmOverride,
    int SortOrder,
    Guid? StockId = null);

public record SpotSelectionInput(
    Guid InkId,
    int Hits,
    decimal CoveragePct,
    int SortOrder);

public record EstimateLineFormInput(
    Guid? Id,
    Guid? SourceProductId,
    string ProductDescription,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    decimal GutterAcrossIn,
    decimal GutterAroundIn,
    decimal BleedIn,
    Guid SubstrateId,
    InkSet InkSet,
    int WhiteHits,
    decimal WhiteCoveragePct,
    IReadOnlyList<SpotSelectionInput> Spots,
    IReadOnlyList<FinishingOperationSelectionInput> FinishingOperations,
    decimal SetupWasteImpressions,
    decimal RunningWastePct,
    string? LineNotes,
    IReadOnlyList<int> Quantities,
    decimal? MarkupPctOverride,
    int? MaxLabelsAcrossOverride,
    LabelOrientation? LabelOrientationOverride,
    IReadOnlyDictionary<int, decimal>? QuantityMarkupOverrides = null);

public record EstimateFormInput(
    Guid CustomerId,
    Guid? SalesRepId,
    string? Notes,
    DateOnly? ValidUntilDate,
    IReadOnlyList<EstimateLineFormInput> Lines,
    Guid? ShippingMethodId,
    decimal ShippingCost,
    ShippingAddress ShippingAddress);

public record ImpositionLayoutView(
    decimal PressWebWidthIn,
    decimal PressMaxImageWidthIn,
    decimal PressFrameRepeatIn,
    decimal PressMaxImageLengthIn,
    decimal PressEdgeMarginIn,
    decimal StockWidthIn,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal GutterAcrossIn,
    decimal GutterAroundIn);

public record EstimateLineCalculationResponse(
    int LineIndex,
    IReadOnlyList<QuantityBreakResult> QuantityBreaks,
    ImpositionResult? Imposition,
    ImpositionLayoutView? ImpositionLayout,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    decimal MarkupPctUsed);

public record EstimateCalculationResponse(
    IReadOnlyList<EstimateLineCalculationResponse> Lines);

public class EstimateCalculationMapper(LabelsMisDbContext db)
{
    private const decimal PressEdgeMarginIn = 0.25m;
    private const decimal MinimumMarginPct = 0.25m;

    private static readonly JsonSerializerOptions FinishingJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<EstimateRequest> BuildRequestAsync(
        Guid customerId,
        EstimateLineFormInput line,
        CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.AsNoTracking()
            .SingleAsync(c => c.Id == customerId, cancellationToken);

        var press = await db.Presses.AsNoTracking()
            .SingleAsync(p => p.Id == Press.Indigo6800Id, cancellationToken);

        var stock = await db.Stocks.AsNoTracking()
            .SingleAsync(s => s.Id == line.SubstrateId, cancellationToken);

        var inkSet = EstimateInkSetMapper.ToIndigoInkSet(line.InkSet);
        var clickRate = await GetClickRateAsync(line.InkSet, cancellationToken);
        var specialInks = await BuildSpecialInksAsync(line, cancellationToken);

        var operationIds = line.FinishingOperations.Select(o => o.OperationId).ToList();
        var operations = await db.FinishingOperations.AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var finishingStockIds = line.FinishingOperations
            .Where(o => o.StockId is { } sid && sid != Guid.Empty)
            .Select(o => o.StockId!.Value)
            .Distinct()
            .ToList();
        var finishingStocks = await db.Stocks.AsNoTracking()
            .Where(s => finishingStockIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var finishingRequests = line.FinishingOperations
            .OrderBy(o => o.SortOrder)
            .Select(selection =>
            {
                var op = operations[selection.OperationId];

                // Only lamination and foil consume an assigned stock.
                Stock? material = null;
                if (op.OperationType is FinishingOperationType.Laminate or FinishingOperationType.Foil
                    && selection.StockId is { } stockId)
                {
                    finishingStocks.TryGetValue(stockId, out material);
                }

                return new FinishingOperationRequest(
                    op.Id,
                    selection.SetupMinutesOverride ?? op.DefaultSetupMinutes,
                    selection.RunSpeedFpmOverride ?? op.DefaultRunSpeedFpm,
                    op.CostPerHour,
                    op.Description,
                    material?.Id,
                    material?.WidthIn,
                    material?.CostPerMsi,
                    material?.Code);
            })
            .ToList();

        return new EstimateRequest(
            line.LabelAcrossIn,
            line.LabelAroundIn,
            line.CornerRadiusIn,
            line.GutterAcrossIn,
            line.GutterAroundIn,
            line.BleedIn,
            press.Id,
            press.WebWidthIn,
            press.MaxImageWidthIn,
            press.FrameRepeatIn,
            press.MaxImageLengthIn,
            PressEdgeMarginIn,
            press.SetupMinutes,
            press.CostPerHour,
            press.SpeedFpm,
            press.IsClickBased,
            inkSet,
            clickRate,
            specialInks,
            stock.Id,
            stock.WidthIn,
            stock.CostPerMsi,
            finishingRequests,
            line.Quantities.Where(q => q > 0).Distinct().OrderBy(q => q).ToList(),
            line.SetupWasteImpressions,
            line.RunningWastePct,
            line.MarkupPctOverride ?? customer.DefaultMarkupPct,
            MinimumMarginPct,
            line.MaxLabelsAcrossOverride,
            line.LabelOrientationOverride,
            line.QuantityMarkupOverrides);
    }

    private async Task<decimal> GetClickRateAsync(
        InkSet inkSet,
        CancellationToken cancellationToken)
    {
        var rate = await LookupProcessClickRateAsync(inkSet, cancellationToken);

        // The process separations are the same four physical CMYK inks whichever CMYK-family
        // set the job runs (white/spots are charged separately as special inks), so a set
        // without its own non-spot ink row — e.g. CMYKW where only the spot White is
        // configured — falls back to the plain CMYK rate instead of charging $0. EPM is
        // excluded: it has a genuinely different (lower) click rate.
        if (rate is null or 0m && inkSet is InkSet.CMYK_PlusSpot or InkSet.CMYKW or InkSet.CMYKW_PlusSpot)
        {
            rate = await LookupProcessClickRateAsync(InkSet.CMYK, cancellationToken);
        }

        return rate ?? 0m;
    }

    private async Task<decimal?> LookupProcessClickRateAsync(
        InkSet inkSet,
        CancellationToken cancellationToken) =>
        await db.Inks.AsNoTracking()
            .Where(i => i.IsActive && i.InkSet == inkSet && !i.IsSpot)
            .OrderBy(i => i.Code)
            .Select(i => (decimal?)i.ClickRatePer1000)
            .FirstOrDefaultAsync(cancellationToken);

    // White (driven by the W in the ink set) and each selected spot are all priced
    // uniformly as hit-based special inks.
    private async Task<IReadOnlyList<SpecialInkSpec>> BuildSpecialInksAsync(
        EstimateLineFormInput line,
        CancellationToken cancellationToken)
    {
        var specialInks = new List<SpecialInkSpec>();

        var whiteAllowed = line.InkSet is InkSet.CMYKW or InkSet.CMYKW_PlusSpot;
        var spotsAllowed = line.InkSet is InkSet.CMYK_PlusSpot or InkSet.CMYKW_PlusSpot;

        if (whiteAllowed && line.WhiteHits > 0)
        {
            var whiteInk = await db.Inks.AsNoTracking()
                .Where(i => i.IsActive && i.IsSpot && i.SpotColor == SpotColor.White)
                .OrderBy(i => i.Code)
                .FirstOrDefaultAsync(cancellationToken);

            var whiteSpec = BuildSpec(whiteInk, "White", line.WhiteHits, line.WhiteCoveragePct);
            if (whiteSpec is not null)
            {
                specialInks.Add(whiteSpec);
            }
        }

        var spots = spotsAllowed
            ? line.Spots.Where(s => s.Hits > 0).OrderBy(s => s.SortOrder).ToList()
            : [];
        if (spots.Count > 0)
        {
            var spotIds = spots.Select(s => s.InkId).ToList();
            var spotInks = await db.Inks.AsNoTracking()
                .Where(i => i.IsActive && i.IsSpot && spotIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, cancellationToken);

            foreach (var spot in spots)
            {
                if (!spotInks.TryGetValue(spot.InkId, out var ink))
                {
                    continue;
                }

                var label = ink.SpotColor?.ToString() ?? ink.Description;
                var spec = BuildSpec(ink, label, spot.Hits, spot.CoveragePct);
                if (spec is not null)
                {
                    specialInks.Add(spec);
                }
            }
        }

        return specialInks;
    }

    private static SpecialInkSpec? BuildSpec(Ink? ink, string label, int hits, decimal coveragePct)
    {
        if (ink is null || hits <= 0)
        {
            return null;
        }

        var coverage = coveragePct > 0 ? coveragePct : ink.DefaultCoveragePct;
        var speedOverride = hits switch
        {
            1 => ink.SpeedFpm1Hit,
            2 => ink.SpeedFpm2Hit,
            >= 3 => ink.SpeedFpm3Hit,
            _ => null
        };
        return new SpecialInkSpec(
            label,
            hits,
            ink.ClickRatePer1000,
            coverage,
            ink.BottleCost,
            ink.BottleSizeMl,
            ink.MlPer1000SqIn,
            speedOverride);
    }

    public static string SerializeFinishingOperations(IReadOnlyList<FinishingOperationSelectionInput> operations) =>
        JsonSerializer.Serialize(operations.OrderBy(o => o.SortOrder));

    public static IReadOnlyList<FinishingOperationSelectionInput> DeserializeFinishingOperations(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<FinishingOperationSelectionInput>>(json, FinishingJsonOptions) ?? [];
    }

    public static string SerializeSpots(IReadOnlyList<SpotSelectionInput> spots) =>
        JsonSerializer.Serialize(spots.OrderBy(s => s.SortOrder));

    public static IReadOnlyList<SpotSelectionInput> DeserializeSpots(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<SpotSelectionInput>>(json, FinishingJsonOptions) ?? [];
    }

    public static string SerializeCostBreakdown(IReadOnlyList<EstimateLineItem> items) =>
        JsonSerializer.Serialize(items);
}
