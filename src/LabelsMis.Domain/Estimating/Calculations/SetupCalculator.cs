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

        var effectiveSpeedFpm = ResolveEffectiveSpeedFpm(request, out var slowedByLabel);

        var runMinutes = effectiveSpeedFpm > 0
            ? EstimatingMath.RoundTwoDecimals(totalWebLengthFt / effectiveSpeedFpm)
            : 0m;

        var runLaborCost = EstimatingMath.RoundCurrency(
            (runMinutes / 60m) * request.PressCostPerHour);

        var runDescription = slowedByLabel is null
            ? "Press run labor"
            : $"Press run labor (slowed to {effectiveSpeedFpm:0.##} fpm for {slowedByLabel})";

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
                runDescription,
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

    // Spot inks (white, silver, named spots) can force the press to run slower. The
    // slowest applicable speed governs the whole run; a null/zero override means no slowdown.
    private static decimal ResolveEffectiveSpeedFpm(EstimateRequest request, out string? slowedByLabel)
    {
        var effective = request.PressSpeedFpm;
        slowedByLabel = null;

        foreach (var ink in request.SpecialInks)
        {
            if (ink.SpeedFpmOverride is > 0 and var speed && speed < effective)
            {
                effective = speed;
                slowedByLabel = ink.Label;
            }
        }

        return effective;
    }
}
