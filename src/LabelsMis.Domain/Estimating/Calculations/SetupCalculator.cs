using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Estimating.Calculations;

internal sealed record PressLaborResult(
    decimal RunTimeMinutes,
    decimal SetupLaborCost,
    decimal RunLaborCost,
    decimal TotalLaborCost,
    IReadOnlyList<EstimateLineItem> LineItems);

internal static class SetupCalculator
{
    public static PressLaborResult CalculatePressLabor(
        EstimateRequest request,
        decimal totalWebLengthFt)
    {
        var setupLaborCost = EstimatingMath.RoundCurrency(
            (request.PressSetupMinutes / 60m) * request.PressCostPerHour);

        var runMinutes = request.PressSpeedFpm > 0
            ? EstimatingMath.RoundTwoDecimals(totalWebLengthFt / request.PressSpeedFpm)
            : 0m;

        var runLaborCost = EstimatingMath.RoundCurrency(
            (runMinutes / 60m) * request.PressCostPerHour);

        var totalMinutes = EstimatingMath.RoundMoney(request.PressSetupMinutes + runMinutes);
        var lineItems = new List<EstimateLineItem>
        {
            new(
                "Press setup",
                "Press setup labor",
                request.PressSetupMinutes,
                "minutes",
                EstimatingMath.RoundMoney(request.PressCostPerHour / 60m),
                setupLaborCost),
            new(
                "Press run",
                "Press run labor",
                runMinutes,
                "minutes",
                EstimatingMath.RoundMoney(request.PressCostPerHour / 60m),
                runLaborCost)
        };

        return new PressLaborResult(
            totalMinutes,
            setupLaborCost,
            runLaborCost,
            setupLaborCost + runLaborCost,
            lineItems);
    }
}
