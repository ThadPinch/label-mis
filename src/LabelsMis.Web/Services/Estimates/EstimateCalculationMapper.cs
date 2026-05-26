using System.Text.Json;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating.Models;
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
    bool WhiteInkUsed,
    IReadOnlyList<FinishingOperationSelectionInput> FinishingOperations,
    decimal SetupWasteImpressions,
    decimal RunningWastePct,
    string? LineNotes,
    IReadOnlyList<int> Quantities,
    decimal? MarkupPctOverride);

public record EstimateFormInput(
    Guid CustomerId,
    Guid? SalesRepId,
    string? Notes,
    DateOnly? ValidUntilDate,
    IReadOnlyList<EstimateLineFormInput> Lines);

public record EstimateLineCalculationResponse(
    int LineIndex,
    IReadOnlyList<QuantityBreakResult> QuantityBreaks,
    ImpositionResult? Imposition,
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
        var clickRate = await GetClickRateAsync(line.InkSet, isWhite: false, cancellationToken);
        var whiteClickRate = line.WhiteInkUsed
            ? await GetClickRateAsync(InkSet.CMYKW, isWhite: true, cancellationToken)
            : 0m;

        var operationIds = line.FinishingOperations.Select(o => o.OperationId).ToList();
        var operations = await db.FinishingOperations.AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var finishingRequests = line.FinishingOperations
            .OrderBy(o => o.SortOrder)
            .Select(selection =>
            {
                var op = operations[selection.OperationId];
                return new FinishingOperationRequest(
                    op.Id,
                    selection.SetupMinutesOverride ?? op.DefaultSetupMinutes,
                    selection.RunSpeedFpmOverride ?? op.DefaultRunSpeedFpm,
                    op.CostPerHour,
                    op.Description);
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
            PressEdgeMarginIn,
            press.SetupMinutes,
            press.CostPerHour,
            press.SpeedFpm,
            press.IsClickBased,
            inkSet,
            clickRate,
            line.WhiteInkUsed,
            whiteClickRate,
            stock.Id,
            stock.WidthIn,
            stock.CostPerMsi,
            finishingRequests,
            line.Quantities.Where(q => q > 0).Distinct().OrderBy(q => q).ToList(),
            line.SetupWasteImpressions,
            line.RunningWastePct,
            line.MarkupPctOverride ?? customer.DefaultMarkupPct,
            MinimumMarginPct);
    }

    private async Task<decimal> GetClickRateAsync(
        InkSet inkSet,
        bool isWhite,
        CancellationToken cancellationToken)
    {
        var ink = await db.Inks.AsNoTracking()
            .Where(i => i.IsActive && i.InkSet == inkSet && i.IsWhite == isWhite)
            .OrderBy(i => i.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return ink?.ClickRatePer1000 ?? 0m;
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

    public static string SerializeCostBreakdown(IReadOnlyList<EstimateLineItem> items) =>
        JsonSerializer.Serialize(items);
}
