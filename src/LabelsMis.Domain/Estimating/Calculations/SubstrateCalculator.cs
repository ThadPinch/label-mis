using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Estimating.Calculations;

internal sealed record SubstrateCostResult(
    decimal TotalWebLengthFt,
    decimal SubstrateCost,
    EstimateLineItem LineItem);

internal static class SubstrateCalculator
{
    public static SubstrateCostResult Calculate(
        EstimateRequest request,
        int impressions,
        decimal repeatLengthIn)
    {
        var totalWebLengthIn = impressions * repeatLengthIn;
        var totalWebLengthFt = EstimatingMath.RoundOneDecimal(totalWebLengthIn / 12m);
        var totalMsi = EstimatingMath.RoundUpOneDecimal(
            (totalWebLengthIn * request.StockWidthIn) / 1000m);
        var substrateCost = EstimatingMath.RoundCurrency(
            totalMsi * request.StockCostPerMsi);

        var lineItem = new EstimateLineItem(
            "Substrate",
            "Stock consumption",
            totalMsi,
            "MSI",
            EstimatingMath.RoundMoney(request.StockCostPerMsi),
            substrateCost);

        return new SubstrateCostResult(totalWebLengthFt, substrateCost, lineItem);
    }
}
