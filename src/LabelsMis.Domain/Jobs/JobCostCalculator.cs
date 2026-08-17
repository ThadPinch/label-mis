namespace LabelsMis.Domain.Jobs;

public record TimeEntryCost(decimal Hours, decimal CostPerHour, decimal LaborCost);

public record MaterialUsageCost(decimal QuantityLf, decimal CostPerLf, decimal MaterialCost);

public record JobActualCostResult(
    decimal TotalLaborCost,
    decimal TotalMaterialCost,
    decimal TotalCost,
    IReadOnlyList<TimeEntryCost> LaborBreakdown,
    IReadOnlyList<MaterialUsageCost> MaterialBreakdown,
    decimal TotalOutsideCost = 0m);

public static class JobCostCalculator
{
    /// <param name="outsideCost">What an outside vendor charged for an outsourced job (the whole item).</param>
    public static JobActualCostResult Calculate(
        IReadOnlyList<(decimal Hours, decimal CostPerHour)> laborEntries,
        IReadOnlyList<(decimal QuantityLf, decimal CostPerLf)> materialEntries,
        decimal outsideCost = 0m)
    {
        var laborBreakdown = laborEntries
            .Select(e => new TimeEntryCost(e.Hours, e.CostPerHour, Round(e.Hours * e.CostPerHour)))
            .ToList();

        var materialBreakdown = materialEntries
            .Select(e => new MaterialUsageCost(e.QuantityLf, e.CostPerLf, Round(e.QuantityLf * e.CostPerLf)))
            .ToList();

        var totalLabor = laborBreakdown.Sum(l => l.LaborCost);
        var totalMaterial = materialBreakdown.Sum(m => m.MaterialCost);

        var totalOutside = Round(Math.Max(0m, outsideCost));

        return new JobActualCostResult(
            totalLabor,
            totalMaterial,
            Round(totalLabor + totalMaterial + totalOutside),
            laborBreakdown,
            materialBreakdown,
            totalOutside);
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
