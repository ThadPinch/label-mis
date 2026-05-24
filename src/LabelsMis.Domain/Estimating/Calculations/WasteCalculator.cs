namespace LabelsMis.Domain.Estimating.Calculations;

internal static class WasteCalculator
{
    public static int CalculateImpressions(
        int quantity,
        decimal runningWastePct,
        int labelsPerImpression,
        decimal setupWasteImpressions)
    {
        var overrunFactor = 1.0m + runningWastePct;
        var productionImpressions = EstimatingMath.CeilingDivision(
            quantity * overrunFactor,
            labelsPerImpression);

        return productionImpressions + (int)Math.Ceiling(setupWasteImpressions);
    }
}
