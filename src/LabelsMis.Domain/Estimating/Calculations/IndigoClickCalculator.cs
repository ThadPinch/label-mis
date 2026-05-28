using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Estimating.Calculations;

internal sealed record ClickCostResult(
    decimal TotalClickCost,
    IReadOnlyList<EstimateLineItem> LineItems,
    IReadOnlyList<string> Warnings);

internal static class IndigoClickCalculator
{
    public static ClickCostResult Calculate(
        EstimateRequest request,
        int impressions,
        int framesPerImpression)
    {
        var warnings = new List<string>();
        var lineItems = new List<EstimateLineItem>();

        if (!request.PressClickBased)
        {
            return new ClickCostResult(0m, lineItems, warnings);
        }

        var frameSlots = impressions * Math.Max(1, framesPerImpression);
        var colorSeparations = IndigoInkSeparations.ColorSeparationsPerFrame(request.InkSet);
        var colorClicks = frameSlots * colorSeparations;

        var clickCost = EstimatingMath.RoundCurrency(
            (colorClicks / 1000m) * request.ClickRatePer1000);

        lineItems.Add(new EstimateLineItem(
            "Press click",
            $"Indigo {request.InkSet} ({colorSeparations} colors × {frameSlots} frame slots)",
            colorClicks,
            "clicks",
            EstimatingMath.RoundMoney(request.ClickRatePer1000 / 1000m),
            clickCost));

        var totalClickCost = clickCost;

        if (request.WhiteInkUsed)
        {
            var whiteClicks = frameSlots;
            var whiteClickCost = EstimatingMath.RoundCurrency(
                (whiteClicks / 1000m) * request.WhiteClickRatePer1000);

            lineItems.Add(new EstimateLineItem(
                "Press click",
                "White ink (1 separation × frame slots)",
                whiteClicks,
                "clicks",
                EstimatingMath.RoundMoney(request.WhiteClickRatePer1000 / 1000m),
                whiteClickCost));

            totalClickCost += whiteClickCost;
        }

        return new ClickCostResult(totalClickCost, lineItems, warnings);
    }
}
