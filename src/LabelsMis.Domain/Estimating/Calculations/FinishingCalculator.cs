using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Estimating.Calculations;

internal sealed record FinishingCostResult(
    decimal TotalFinishingCost,
    IReadOnlyList<EstimateLineItem> LineItems);

internal static class FinishingCalculator
{
    public static FinishingCostResult Calculate(
        IReadOnlyList<FinishingOperationRequest> operations,
        decimal totalWebLengthFt)
    {
        var lineItems = new List<EstimateLineItem>();
        var totalCost = 0m;

        foreach (var operation in operations)
        {
            var runMinutes = operation.RunSpeedFpm > 0
                ? EstimatingMath.RoundTwoDecimals(totalWebLengthFt / operation.RunSpeedFpm)
                : 0m;
            var totalMinutes = EstimatingMath.RoundTwoDecimals(operation.SetupMinutes + runMinutes);
            var operationCost = EstimatingMath.RoundCurrency(
                (totalMinutes / 60m) * operation.CostPerHour);

            lineItems.Add(new EstimateLineItem(
                "Finishing",
                operation.Description,
                totalMinutes,
                "minutes",
                EstimatingMath.RoundMoney(operation.CostPerHour / 60m),
                operationCost));

            totalCost += operationCost;
        }

        return new FinishingCostResult(totalCost, lineItems);
    }
}
