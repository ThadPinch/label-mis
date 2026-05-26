using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Estimating.Calculations;

internal static class ImpositionCalculator
{
    private const decimal LowUtilizationThreshold = 0.50m;

    public sealed record ImpositionCalculation(
        ImpositionResult Result,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings);

    public static ImpositionCalculation Calculate(EstimateRequest request)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var asEntered = TryOrientation(request, LabelOrientation.AsEntered, request.LabelAcrossIn, request.LabelAroundIn);
        var rotated = TryOrientation(request, LabelOrientation.Rotated, request.LabelAroundIn, request.LabelAcrossIn);

        Candidate? chosen;
        if (request.LabelOrientationOverride is LabelOrientation forced)
        {
            chosen = forced == LabelOrientation.Rotated ? rotated : asEntered;
            if (chosen.MaxLabelsAcross < 1)
            {
                errors.Add("Label too wide for press");
                return new ImpositionCalculation(null!, errors, warnings);
            }
        }
        else
        {
            var valid = new List<Candidate>();
            if (asEntered.MaxLabelsAcross >= 1) valid.Add(asEntered);
            if (rotated.MaxLabelsAcross >= 1) valid.Add(rotated);

            if (valid.Count == 0)
            {
                errors.Add("Label too wide for press");
                return new ImpositionCalculation(null!, errors, warnings);
            }

            chosen = valid
                .OrderByDescending(c => c.MaxLabelsAcross)
                .ThenBy(c => c.AroundIn)
                .ThenBy(c => c.Orientation)
                .First();
        }

        var maxAcross = chosen.MaxLabelsAcross;
        var clampedAcross = request.MaxLabelsAcrossOverride is int requested
            ? Math.Clamp(requested, 1, maxAcross)
            : maxAcross;

        const int labelsAround = 1;
        var labelsPerImpression = clampedAcross * labelsAround;
        var repeatLength = labelsAround * (chosen.AroundIn + request.GutterAroundIn);
        var utilizationPct = (clampedAcross * chosen.AcrossIn) / request.PressWebWidthIn;

        if (utilizationPct < LowUtilizationThreshold)
        {
            warnings.Add("Low web utilization, consider gang or different press");
        }

        var result = new ImpositionResult(
            clampedAcross,
            labelsAround,
            labelsPerImpression,
            EstimatingMath.RoundMoney(repeatLength),
            EstimatingMath.RoundMoney(utilizationPct),
            chosen.Orientation,
            maxAcross,
            chosen.AcrossIn,
            chosen.AroundIn);

        return new ImpositionCalculation(result, errors, warnings);
    }

    private static Candidate TryOrientation(
        EstimateRequest request,
        LabelOrientation orientation,
        decimal acrossIn,
        decimal aroundIn)
    {
        var max = 0;
        if (acrossIn + (2 * request.PressEdgeMarginIn) <= request.PressWebWidthIn)
        {
            var numerator = request.PressWebWidthIn
                - (2 * request.PressEdgeMarginIn)
                + request.GutterAcrossIn;
            var denominator = acrossIn + request.GutterAcrossIn;
            max = (int)Math.Floor(numerator / denominator);
        }

        return new Candidate(orientation, acrossIn, aroundIn, Math.Max(0, max));
    }

    private sealed record Candidate(
        LabelOrientation Orientation,
        decimal AcrossIn,
        decimal AroundIn,
        int MaxLabelsAcross);
}
