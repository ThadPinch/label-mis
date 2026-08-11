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
        ImpositionResult imposition)
    {
        var warnings = new List<string>();
        var lineItems = new List<EstimateLineItem>();

        if (!request.PressClickBased)
        {
            return new ClickCostResult(0m, lineItems, warnings);
        }

        // HP bills a flat frame factor's worth of clicks per impression per separation,
        // regardless of how much of the frame the layout occupies.
        var frameFactor = Math.Max(1, request.PressFrameFactor);
        var frameSlots = impressions * frameFactor;
        var colorSeparations = IndigoInkSeparations.ColorSeparationsPerFrame(request.InkSet);
        var colorClicks = frameSlots * colorSeparations;

        var clickCost = EstimatingMath.RoundCurrency(
            (colorClicks / 1000m) * request.ClickRatePer1000);

        lineItems.Add(new EstimateLineItem(
            "Press click",
            $"Indigo {request.InkSet} ({colorSeparations} colors × {impressions} impressions × {frameFactor} frame factor)",
            colorClicks,
            "clicks",
            EstimatingMath.RoundMoney(request.ClickRatePer1000 / 1000m),
            clickCost));

        var totalClickCost = clickCost;

        // Printed label area for ink-consumption costing (across the whole run, incl. waste).
        var jobAreaSqIn = impressions
            * imposition.LabelsPerImpression
            * imposition.EffectiveLabelAcrossIn
            * imposition.EffectiveLabelAroundIn;

        foreach (var specialInk in request.SpecialInks)
        {
            totalClickCost += AddSpecialInk(specialInk, frameSlots, jobAreaSqIn, lineItems);
        }

        return new ClickCostResult(totalClickCost, lineItems, warnings);
    }

    private static decimal AddSpecialInk(
        SpecialInkSpec? spec,
        int frameSlots,
        decimal jobAreaSqIn,
        List<EstimateLineItem> lineItems)
    {
        if (spec is null || spec.Hits <= 0)
        {
            return 0m;
        }

        var added = 0m;

        // One click per hit, per frame slot.
        var clicks = frameSlots * spec.Hits;
        var clickCost = EstimatingMath.RoundCurrency((clicks / 1000m) * spec.ClickRatePer1000);
        lineItems.Add(new EstimateLineItem(
            "Press click",
            $"{spec.Label} ({spec.Hits} hit{(spec.Hits == 1 ? "" : "s")} × {frameSlots} click frames)",
            clicks,
            "clicks",
            EstimatingMath.RoundMoney(spec.ClickRatePer1000 / 1000m),
            clickCost));
        added += clickCost;

        // Ink consumption from the bottle, scaled by coverage and hits.
        if (spec.BottleSizeMl > 0 && spec.BottleCost > 0 && spec.MlPer1000SqIn > 0)
        {
            var mlUsed = (jobAreaSqIn / 1000m) * spec.MlPer1000SqIn * spec.CoveragePct * spec.Hits;
            var costPerMl = spec.BottleCost / spec.BottleSizeMl;
            var inkCost = EstimatingMath.RoundCurrency(costPerMl * mlUsed);
            if (inkCost > 0)
            {
                lineItems.Add(new EstimateLineItem(
                    "Ink",
                    $"{spec.Label} ink ({spec.CoveragePct * 100m:0.#}% coverage × {spec.Hits} hit{(spec.Hits == 1 ? "" : "s")})",
                    EstimatingMath.RoundTwoDecimals(mlUsed),
                    "mL",
                    EstimatingMath.RoundMoney(costPerMl),
                    inkCost));
                added += inkCost;
            }
        }

        return added;
    }
}
