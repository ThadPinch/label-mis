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

    /// <summary>A click-based press can't be priced without a click rate for the line's ink set:
    /// costing the clicks at $0 under-quotes silently (the EPM gap), so it is an error.</summary>
    public static IReadOnlyList<string> ValidateClickRate(EstimateRequest request) =>
        request.PressClickBased && request.ClickRateMissing
            ? [$"No ink record for {request.InkSet} — clicks can't be costed. Add one in Setup → Inks."]
            : [];

    /// <summary>Lamination/foil with no stock selected prices the pass but not the material.
    /// Sometimes legitimate (customer-supplied film), so it is a warning, not an error.</summary>
    public static IReadOnlyList<string> ValidateFinishingMaterials(EstimateRequest request) =>
        request.FinishingOperations
            .Where(o => o.MaterialExpected && !(o.StockWidthIn is > 0 && o.StockCostPerMsi is not null))
            .Select(o => $"{o.Description}: no stock selected — its material isn't costed.")
            .ToList();

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
