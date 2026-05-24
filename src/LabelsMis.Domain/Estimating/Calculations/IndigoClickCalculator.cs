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
        int impressions)
    {
        var warnings = new List<string>();
        var lineItems = new List<EstimateLineItem>();

        if (!request.PressClickBased)
        {
            return new ClickCostResult(0m, lineItems, warnings);
        }

        var clickCost = EstimatingMath.RoundCurrency(
            (impressions / 1000m) * request.ClickRatePer1000);

        lineItems.Add(new EstimateLineItem(
            "Press click",
            $"Indigo {request.InkSet} click charge",
            impressions,
            "impressions",
            EstimatingMath.RoundMoney(request.ClickRatePer1000 / 1000m),
            clickCost));

        var totalClickCost = clickCost;

        if (request.WhiteInkUsed)
        {
            var whiteClickCost = EstimatingMath.RoundCurrency(
                (impressions / 1000m) * request.WhiteClickRatePer1000);

            lineItems.Add(new EstimateLineItem(
                "Press click",
                "White ink click charge",
                impressions,
                "impressions",
                EstimatingMath.RoundMoney(request.WhiteClickRatePer1000 / 1000m),
                whiteClickCost));

            totalClickCost += whiteClickCost;
        }

        return new ClickCostResult(totalClickCost, lineItems, warnings);
    }
}
