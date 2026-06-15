using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Estimating.Rules;

internal static class PressRules
{
    public static IReadOnlyList<string> ValidateInkConfiguration(EstimateRequest request)
    {
        var warnings = new List<string>();

        if (!request.PressClickBased && request.ClickRatePer1000 > 0)
        {
            warnings.Add("Click rate provided but press is not click-based");
        }

        return warnings;
    }

    public static IReadOnlyList<string> ValidateQuantities(IReadOnlyList<int> quantities)
    {
        var errors = new List<string>();

        if (quantities.Count == 0)
        {
            errors.Add("At least one quantity is required");
            return errors;
        }

        foreach (var quantity in quantities)
        {
            if (quantity <= 0)
            {
                errors.Add("Quantity must be greater than zero");
                break;
            }
        }

        return errors;
    }
}
