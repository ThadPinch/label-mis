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

        if (request.LabelAcrossIn + (2 * request.PressEdgeMarginIn) > request.PressWebWidthIn)
        {
            errors.Add("Label too wide for press");
        }

        var labelsAcross = CalculateLabelsAcross(request);
        if (labelsAcross < 1)
        {
            errors.Add("Label too wide for press");
        }

        if (errors.Count > 0)
        {
            return new ImpositionCalculation(null!, errors, warnings);
        }

        const int labelsAround = 1;
        var labelsPerImpression = labelsAcross * labelsAround;
        var repeatLength = labelsAround * (request.LabelAroundIn + request.GutterAroundIn);
        var utilizationPct = (labelsAcross * request.LabelAcrossIn) / request.PressWebWidthIn;

        if (utilizationPct < LowUtilizationThreshold)
        {
            warnings.Add("Low web utilization, consider gang or different press");
        }

        var result = new ImpositionResult(
            labelsAcross,
            labelsAround,
            labelsPerImpression,
            EstimatingMath.RoundMoney(repeatLength),
            EstimatingMath.RoundMoney(utilizationPct));

        return new ImpositionCalculation(result, errors, warnings);
    }

    private static int CalculateLabelsAcross(EstimateRequest request)
    {
        var numerator = request.PressWebWidthIn
            - (2 * request.PressEdgeMarginIn)
            + request.GutterAcrossIn;
        var denominator = request.LabelAcrossIn + request.GutterAcrossIn;

        return (int)Math.Floor(numerator / denominator);
    }
}
