namespace LabelsMis.Domain.Jobs;

public record TimeEntryCost(decimal Hours, decimal CostPerHour, decimal LaborCost);

public record MaterialUsageCost(decimal QuantityLf, decimal CostPerLf, decimal MaterialCost);

public record JobActualCostResult(
    decimal TotalLaborCost,
    decimal TotalMaterialCost,
    decimal TotalCost,
    IReadOnlyList<TimeEntryCost> LaborBreakdown,
    IReadOnlyList<MaterialUsageCost> MaterialBreakdown);

public static class JobCostCalculator
{
    public static JobActualCostResult Calculate(
        IReadOnlyList<(decimal Hours, decimal CostPerHour)> laborEntries,
        IReadOnlyList<(decimal QuantityLf, decimal CostPerLf)> materialEntries)
    {
        var laborBreakdown = laborEntries
            .Select(e => new TimeEntryCost(e.Hours, e.CostPerHour, Round(e.Hours * e.CostPerHour)))
            .ToList();

        var materialBreakdown = materialEntries
            .Select(e => new MaterialUsageCost(e.QuantityLf, e.CostPerLf, Round(e.QuantityLf * e.CostPerLf)))
            .ToList();

        var totalLabor = laborBreakdown.Sum(l => l.LaborCost);
        var totalMaterial = materialBreakdown.Sum(m => m.MaterialCost);

        return new JobActualCostResult(
            totalLabor,
            totalMaterial,
            Round(totalLabor + totalMaterial),
            laborBreakdown,
            materialBreakdown);
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
