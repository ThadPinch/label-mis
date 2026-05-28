using LabelsMis.Domain.Estimating.Calculations;
using LabelsMis.Domain.Estimating.Models;
using LabelsMis.Domain.Estimating.Rules;

namespace LabelsMis.Domain.Estimating;

public class EstimatingService
{
    public EstimateResult Calculate(EstimateRequest request)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        errors.AddRange(PressRules.ValidateQuantities(request.Quantities));
        warnings.AddRange(PressRules.ValidateInkConfiguration(request));

        if (errors.Count > 0)
        {
            return new EstimateResult(null, [], warnings, errors);
        }

        var impositionCalculation = ImpositionCalculator.Calculate(request);
        errors.AddRange(impositionCalculation.Errors);
        warnings.AddRange(impositionCalculation.Warnings);

        if (errors.Count > 0)
        {
            return new EstimateResult(null, [], warnings, errors);
        }

        var imposition = impositionCalculation.Result;
        var quantityBreaks = new List<QuantityBreakResult>();

        foreach (var quantity in request.Quantities.OrderBy(q => q))
        {
            quantityBreaks.Add(CalculateQuantityBreak(request, imposition, quantity));
        }

        return new EstimateResult(imposition, quantityBreaks, warnings, errors);
    }

    private static QuantityBreakResult CalculateQuantityBreak(
        EstimateRequest request,
        ImpositionResult imposition,
        int quantity)
    {
        var impressions = WasteCalculator.CalculateImpressions(
            quantity,
            request.RunningWastePct,
            imposition.LabelsPerImpression,
            request.SetupWasteImpressions);

        var clickResult = IndigoClickCalculator.Calculate(
            request,
            impressions,
            imposition.FramesPerImpression);
        var substrateResult = SubstrateCalculator.Calculate(
            request,
            impressions,
            imposition.RepeatLengthIn);
        var pressLaborResult = SetupCalculator.CalculatePressLabor(
            request,
            substrateResult.TotalWebLengthFt);
        var finishingResult = FinishingCalculator.Calculate(
            request.FinishingOperations,
            substrateResult.TotalWebLengthFt);

        var costBreakdown = new List<EstimateLineItem>();
        costBreakdown.AddRange(clickResult.LineItems);
        costBreakdown.Add(substrateResult.LineItem);
        costBreakdown.AddRange(pressLaborResult.LineItems);
        costBreakdown.AddRange(finishingResult.LineItems);

        var totalCost = EstimatingMath.RoundCurrency(
            clickResult.TotalClickCost
            + substrateResult.SubstrateCost
            + pressLaborResult.TotalLaborCost
            + finishingResult.TotalFinishingCost);

        var pricing = MarkupRules.Calculate(
            totalCost,
            quantity,
            request.CustomerMarkupPct,
            request.MinimumMarginPct);

        return new QuantityBreakResult(
            quantity,
            impressions,
            substrateResult.TotalWebLengthFt,
            pressLaborResult.RunTimeMinutes,
            totalCost,
            pricing.TotalPrice,
            pricing.UnitPrice,
            pricing.PricePerThousand,
            pricing.MarginPct,
            pricing.BelowMinimumMargin,
            costBreakdown);
    }
}
