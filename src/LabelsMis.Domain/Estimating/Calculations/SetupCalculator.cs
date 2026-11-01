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

        var effectiveSpeedFpm = ResolveEffectiveSpeedFpm(request, out var runNote);

        var runMinutes = effectiveSpeedFpm > 0
            ? EstimatingMath.RoundTwoDecimals(totalWebLengthFt / effectiveSpeedFpm)
            : 0m;

        var runLaborCost = EstimatingMath.RoundCurrency(
            (runMinutes / 60m) * request.PressCostPerHour);

        var runDescription = runNote is null
            ? "Press run labor"
            : $"Press run labor ({runNote})";

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

    // Base speed: the press's speed for the line's separation count (ink-set colours + white and
    // spot hits, 5+ sharing one speed) when that speed is configured, otherwise its single speed.
    // Spot inks (white, silver, named spots) can then force the press slower: the slowest
    // applicable speed governs the whole run; a null/zero override means no slowdown. On a
    // separation-speed press the white's own slowdown is skipped — its pass is already one of
    // the counted separations (EPMW + 1 white = 4 separations runs at the 4-separation speed).
    private static decimal ResolveEffectiveSpeedFpm(EstimateRequest request, out string? runNote)
    {
        var separations = IndigoInkSeparations.ColorSeparationsPerFrame(request.InkSet)
            + request.SpecialInks.Where(i => i is { Hits: > 0 }).Sum(i => i.Hits);
        var separationSpeed = Math.Clamp(separations, 3, 5) switch
        {
            3 => request.PressSpeedFpm3Separations,
            4 => request.PressSpeedFpm4Separations,
            _ => request.PressSpeedFpm5PlusSeparations
        };
        var bySeparations = separationSpeed is > 0;

        var effective = bySeparations ? separationSpeed!.Value : request.PressSpeedFpm;
        runNote = bySeparations ? $"{separations} separations at {effective:0.##} fpm" : null;

        foreach (var ink in request.SpecialInks)
        {
            if (bySeparations && ink.IsWhite)
            {
                continue;
            }

            if (ink.SpeedFpmOverride is > 0 and var speed && speed < effective)
            {
                effective = speed;
                runNote = $"slowed to {effective:0.##} fpm for {ink.Label}";
            }
        }

        return effective;
    }
}
