using System.Text;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Jobs;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Reports;

public record EvaRow(
    Guid JobId,
    string JobNumber,
    string CustomerName,
    string ProductDescription,
    JobStatus Status,
    int QuantityOrdered,
    int GoodCount,
    int WasteCount,
    decimal Revenue,
    decimal? EstimatedCost,
    decimal ActualLaborCost,
    decimal ActualMaterialCost,
    decimal ActualTotalCost,
    decimal? CostVariance,
    decimal? CostVariancePct,
    decimal? EstimatedMarginPct,
    decimal? ActualMarginPct);

public record EvaSummary(
    int JobCount,
    decimal TotalRevenue,
    decimal TotalEstimated,
    decimal TotalActual,
    decimal TotalVariance,
    decimal? ActualMarginPct);

public record EvaReport(EvaSummary Summary, IReadOnlyList<EvaRow> Rows);

/// <summary>
/// Estimate vs Actual job costing: for each job, the quoted cost from its estimate quantity
/// break, the actual labor + material cost from recorded time entries and roll usage, the
/// revenue from its order line, and the resulting variances and margins.
/// </summary>
public class EvaService(LabelsMisDbContext db)
{
    public async Task<EvaReport> GetAsync(
        DateOnly from,
        DateOnly to,
        Guid? customerId,
        bool finishedOnly,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var jobs = await db.Jobs.AsNoTracking()
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder).ThenInclude(o => o.Customer)
            .Include(j => j.Operations).ThenInclude(o => o.TimeEntries)
            .Include(j => j.MaterialUsages).ThenInclude(m => m.Stock)
            .AsSplitQuery()
            .Where(j => j.CreatedAt >= fromUtc && j.CreatedAt < toUtc)
            .Where(j => customerId == null || j.SalesOrderLine.SalesOrder.CustomerId == customerId)
            .Where(j => !finishedOnly || j.Status >= JobStatus.Rewound)
            .ToListAsync(cancellationToken);

        var presses = await db.Presses.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var finishing = await db.FinishingOperations.AsNoTracking().ToDictionaryAsync(f => f.Id, cancellationToken);

        // Estimated cost comes from the sourcing estimate line's quantity break.
        var estimateLineIds = jobs
            .Select(j => j.Product.SourceEstimateLineId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var breaksByLine = (await db.EstimateQuantityBreaks.AsNoTracking()
                .Where(q => estimateLineIds.Contains(q.EstimateLineId))
                .ToListAsync(cancellationToken))
            .ToLookup(q => q.EstimateLineId);

        var rows = jobs.Select(job =>
        {
            var actual = CalculateActual(job, presses, finishing);
            var estimated = EstimateCost(job, breaksByLine);
            var revenue = job.SalesOrderLine.UnitPrice * job.QuantityOrdered;

            decimal? variance = estimated is { } est ? actual.TotalCost - est : null;
            decimal? variancePct = estimated is > 0 ? Math.Round((actual.TotalCost - estimated.Value) / estimated.Value * 100m, 1) : null;
            decimal? estMargin = estimated is { } e && revenue > 0 ? Math.Round((revenue - e) / revenue * 100m, 1) : null;
            decimal? actMargin = revenue > 0 ? Math.Round((revenue - actual.TotalCost) / revenue * 100m, 1) : null;

            return new EvaRow(
                job.Id,
                job.JobNumber,
                job.SalesOrderLine.SalesOrder.Customer.Name,
                job.SalesOrderLine.Description ?? job.Product.Description,
                job.Status,
                job.QuantityOrdered,
                job.Operations.Sum(o => o.GoodCount),
                job.Operations.Sum(o => o.WasteCount),
                revenue,
                estimated,
                actual.TotalLaborCost,
                actual.TotalMaterialCost,
                actual.TotalCost,
                variance,
                variancePct,
                estMargin,
                actMargin);
        }).ToList();

        rows = sort switch
        {
            "margin" => rows.OrderBy(r => r.ActualMarginPct ?? decimal.MaxValue).ToList(),
            "revenue" => rows.OrderByDescending(r => r.Revenue).ToList(),
            "job" => rows.OrderBy(r => r.JobNumber).ToList(),
            _ => rows.OrderByDescending(r => Math.Abs(r.CostVariance ?? 0m)).ToList()
        };

        var totalRevenue = rows.Sum(r => r.Revenue);
        var totalActual = rows.Sum(r => r.ActualTotalCost);
        var summary = new EvaSummary(
            rows.Count,
            totalRevenue,
            rows.Sum(r => r.EstimatedCost ?? 0m),
            totalActual,
            rows.Sum(r => r.CostVariance ?? 0m),
            totalRevenue > 0 ? Math.Round((totalRevenue - totalActual) / totalRevenue * 100m, 1) : null);

        return new EvaReport(summary, rows);
    }

    public static byte[] ToCsv(EvaReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Job,Customer,Product,Status,Qty ordered,Good,Waste,Revenue,Estimated cost,Actual labor,Actual material,Actual total,Variance,Variance %,Est margin %,Actual margin %");
        foreach (var r in report.Rows)
        {
            sb.AppendLine(string.Join(',',
                Csv(r.JobNumber), Csv(r.CustomerName), Csv(r.ProductDescription), r.Status.ToString(),
                r.QuantityOrdered.ToString(), r.GoodCount.ToString(), r.WasteCount.ToString(),
                r.Revenue.ToString(), r.EstimatedCost?.ToString() ?? "", r.ActualLaborCost.ToString(),
                r.ActualMaterialCost.ToString(), r.ActualTotalCost.ToString(),
                r.CostVariance?.ToString() ?? "", r.CostVariancePct?.ToString() ?? "",
                r.EstimatedMarginPct?.ToString() ?? "", r.ActualMarginPct?.ToString() ?? ""));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static JobActualCostResult CalculateActual(
        Job job,
        IReadOnlyDictionary<Guid, Press> presses,
        IReadOnlyDictionary<Guid, FinishingOperation> finishing)
    {
        var laborEntries = new List<(decimal Hours, decimal CostPerHour)>();
        foreach (var operation in job.Operations)
        {
            // ActualHours prefers the operator-recorded time (which can be under plan — a
            // favorable variance) and falls back to clocked time entries.
            var hours = operation.ActualHours;
            if (hours <= 0)
            {
                continue;
            }

            var costPerHour = operation.EquipmentType switch
            {
                EquipmentType.Press when operation.EquipmentId is Guid pressId && presses.TryGetValue(pressId, out var press)
                    => press.CostPerHour,
                EquipmentType.FinishingOperation when operation.EquipmentId is Guid finId && finishing.TryGetValue(finId, out var fin)
                    => fin.CostPerHour,
                _ => 0m
            };
            laborEntries.Add((hours, costPerHour));
        }

        var materialEntries = job.MaterialUsages
            .Select(m => (m.QuantityUsedLf, m.Stock.CostPerMsi / 1000m * m.Stock.WidthIn))
            .ToList();

        return JobCostCalculator.Calculate(laborEntries, materialEntries);
    }

    private static decimal? EstimateCost(Job job, ILookup<Guid, EstimateQuantityBreak> breaksByLine)
    {
        if (job.Product.SourceEstimateLineId is not Guid lineId)
        {
            return null;
        }

        var breaks = breaksByLine[lineId].ToList();
        if (breaks.Count == 0)
        {
            return null;
        }

        var breakRow = breaks
            .Where(q => q.Quantity >= job.QuantityOrdered)
            .OrderBy(q => q.Quantity)
            .FirstOrDefault()
            ?? breaks.OrderByDescending(q => q.Quantity).First();

        return breakRow.CalculatedCost;
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
