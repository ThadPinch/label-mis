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

        var imageableWidth = GetImageableWidthIn(request);
        var maxRepeat = GetMaxRepeatIn(request);

        var asEntered = TryOrientation(
            request,
            LabelOrientation.AsEntered,
            request.LabelAcrossIn,
            request.LabelAroundIn,
            imageableWidth,
            maxRepeat);
        var rotated = TryOrientation(
            request,
            LabelOrientation.Rotated,
            request.LabelAroundIn,
            request.LabelAcrossIn,
            imageableWidth,
            maxRepeat);

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
                .OrderByDescending(c => c.LabelsPerImpression)
                .ThenByDescending(c => c.MaxLabelsAcross)
                .ThenBy(c => c.LayoutRepeatIn)
                .ThenBy(c => c.Orientation)
                .First();
        }

        var maxAcross = chosen.MaxLabelsAcross;
        var clampedAcross = request.MaxLabelsAcrossOverride is int requested
            ? Math.Clamp(requested, 1, maxAcross)
            : maxAcross;

        var labelsAround = chosen.MaxLabelsAround;
        var labelsPerImpression = clampedAcross * labelsAround;
        var layoutRepeat = labelsAround * (chosen.AroundIn + request.GutterAroundIn);

        // Billable repeat is the web the layout actually occupies, floored at the
        // press's minimum runnable repeat — not rounded up to whole press frames.
        var repeatLengthIn = Math.Max(layoutRepeat, request.PressMinRepeatIn);
        if (maxRepeat > 0 && repeatLengthIn > maxRepeat + 0.0001m)
        {
            errors.Add("Label repeat exceeds maximum press repeat");
            return new ImpositionCalculation(null!, errors, warnings);
        }

        var utilizationPct = (clampedAcross * chosen.AcrossIn) / request.PressWebWidthIn;

        if (utilizationPct < LowUtilizationThreshold)
        {
            warnings.Add("Low web utilization, consider gang or different press");
        }

        var result = new ImpositionResult(
            clampedAcross,
            labelsAround,
            labelsPerImpression,
            EstimatingMath.RoundMoney(layoutRepeat),
            EstimatingMath.RoundMoney(repeatLengthIn),
            EstimatingMath.RoundMoney(utilizationPct),
            chosen.Orientation,
            maxAcross,
            chosen.AcrossIn,
            chosen.AroundIn);

        return new ImpositionCalculation(result, errors, warnings);
    }

    private static decimal GetImageableWidthIn(EstimateRequest request) =>
        request.PressMaxImageWidthIn > 0
            ? request.PressMaxImageWidthIn
            : request.PressWebWidthIn - (2 * request.PressEdgeMarginIn);

    private static decimal GetMaxRepeatIn(EstimateRequest request) =>
        request.PressMaxRepeatIn > 0
            ? request.PressMaxRepeatIn
            : request.PressMaxImageLengthIn;

    private static Candidate TryOrientation(
        EstimateRequest request,
        LabelOrientation orientation,
        decimal acrossIn,
        decimal aroundIn,
        decimal imageableWidth,
        decimal maxRepeat)
    {
        var maxAcross = CalculateMaxLabelsAcross(imageableWidth, acrossIn, request.GutterAcrossIn);
        var maxAround = CalculateMaxLabelsAround(maxRepeat, aroundIn, request.GutterAroundIn);
        var layoutRepeat = maxAround * (aroundIn + request.GutterAroundIn);
        var labelsPerImpression = Math.Max(0, maxAcross) * maxAround;

        return new Candidate(
            orientation,
            acrossIn,
            aroundIn,
            maxAcross,
            maxAround,
            layoutRepeat,
            labelsPerImpression);
    }

    private static int CalculateMaxLabelsAcross(
        decimal imageableWidth,
        decimal acrossIn,
        decimal gutterAcross)
    {
        if (acrossIn <= 0 || imageableWidth <= 0)
        {
            return 0;
        }

        var numerator = imageableWidth + gutterAcross;
        var denominator = acrossIn + gutterAcross;
        return (int)Math.Floor(numerator / denominator);
    }

    private static int CalculateMaxLabelsAround(
        decimal maxRepeat,
        decimal aroundIn,
        decimal gutterAround)
    {
        if (maxRepeat <= 0 || aroundIn <= 0)
        {
            return 1;
        }

        var pitch = aroundIn + gutterAround;
        return Math.Max(1, (int)Math.Floor(maxRepeat / pitch));
    }

    private sealed record Candidate(
        LabelOrientation Orientation,
        decimal AcrossIn,
        decimal AroundIn,
        int MaxLabelsAcross,
        int MaxLabelsAround,
        decimal LayoutRepeatIn,
        int LabelsPerImpression);
}
