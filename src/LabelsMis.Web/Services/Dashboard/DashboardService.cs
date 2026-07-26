using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Dashboard;

public record TimePoint(string Label, decimal Value);

public record CategoryValue(string Label, decimal Value, int Count);

public record RecentOrderItem(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    SalesOrderStatus Status,
    DateTime OrderedAt,
    decimal Value);

public record JobDueItem(
    Guid Id,
    string JobNumber,
    string ProductDescription,
    string CustomerName,
    JobStatus Status,
    DateOnly DueDate,
    bool Overdue);

public record StockLevelRow(
    string Code,
    string Description,
    int RollCount,
    decimal RemainingLf,
    decimal MinOrderQtyLf,
    bool Low);

public record SalesSection(
    int OpenOrderCount,
    decimal OpenOrderValue,
    int InProductionCount,
    IReadOnlyList<TimePoint> OrderIntake,
    IReadOnlyList<RecentOrderItem> RecentOrders);

public record JobsSection(
    int WipCount,
    int OverdueCount,
    IReadOnlyList<CategoryValue> Pipeline,
    IReadOnlyList<JobDueItem> DueSoon);

public record EstimatesSection(
    int OpenCount,
    int WonCount,
    int LostCount,
    decimal? WinRatePct,
    IReadOnlyList<CategoryValue> Funnel);

public record FinanceSection(
    decimal RevenueMtd,
    decimal RevenueLastMonth,
    decimal ArOutstanding,
    int ArOpenInvoiceCount,
    IReadOnlyList<TimePoint> RevenueTrend,
    IReadOnlyList<CategoryValue> ArAging,
    IReadOnlyList<CategoryValue> TopCustomers);

public record InventorySection(
    int AvailableRollCount,
    decimal TotalRemainingLf,
    int LowStockCount,
    int OpenPoCount,
    decimal OpenPoValue,
    IReadOnlyList<StockLevelRow> StockLevels);

public record ShippingSection(
    int PendingCount,
    int InTransitCount,
    int ShippedLast7Days);

public record WorkloadRow(
    string Task,
    string Department,
    int OperationCount,
    int JobCount,
    decimal Hours);

public record WorkloadSection(
    decimal TotalHours,
    int OperationCount,
    int JobCount,
    IReadOnlyList<CategoryValue> ByDepartment,
    IReadOnlyList<WorkloadRow> ByTask);

public record DashboardData(
    DateOnly Today,
    int RangeDays,
    SalesSection? Sales,
    JobsSection? Jobs,
    EstimatesSection? Estimates,
    FinanceSection? Finance,
    InventorySection? Inventory,
    ShippingSection? Shipping,
    WorkloadSection? Workload);

public class DashboardService(LabelsMisDbContext db)
{
    private static readonly JobStatus[] WipStatuses =
        [JobStatus.PrePress, JobStatus.Queued, JobStatus.Printed, JobStatus.Finished, JobStatus.Rewound];

    public async Task<DashboardData> GetAsync(
        int rangeDays,
        bool includeSales,
        bool includeJobs,
        bool includeEstimates,
        bool includeFinance,
        bool includeInventory,
        bool includeShipping,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fromDate = today.AddDays(-rangeDays);
        var fromDateTime = DateTime.UtcNow.AddDays(-rangeDays);

        var sales = includeSales ? await LoadSalesAsync(fromDateTime, rangeDays, today, cancellationToken) : null;
        var jobs = includeJobs ? await LoadJobsAsync(today, cancellationToken) : null;
        var estimates = includeEstimates ? await LoadEstimatesAsync(fromDateTime, cancellationToken) : null;
        var finance = includeFinance ? await LoadFinanceAsync(fromDate, rangeDays, today, cancellationToken) : null;
        var inventory = includeInventory ? await LoadInventoryAsync(cancellationToken) : null;
        var shipping = includeShipping ? await LoadShippingAsync(today, cancellationToken) : null;
        var workload = includeJobs ? await LoadWorkloadAsync(cancellationToken) : null;

        return new DashboardData(today, rangeDays, sales, jobs, estimates, finance, inventory, shipping, workload);
    }

    /// <summary>
    /// Remaining planned hours on active jobs, from each job's pending/in-progress operations —
    /// how much work is queued up per department and per machine/task.
    /// </summary>
    private async Task<WorkloadSection> LoadWorkloadAsync(CancellationToken ct)
    {
        var openStatuses = new[] { JobOperationStatus.Pending, JobOperationStatus.InProgress };
        var openOps = await db.JobOperations
            .Where(o => WipStatuses.Contains(o.Job.Status) && openStatuses.Contains(o.Status))
            .Select(o => new { o.JobId, o.OperationType, o.EquipmentId, o.PlannedMinutes })
            .ToListAsync(ct);

        var pressNames = await db.Presses.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var finishingNames = await db.FinishingOperations.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, f => f.Description, ct);

        JobOperationType[] departmentOrder =
            [JobOperationType.Press, JobOperationType.Finishing, JobOperationType.Inspection, JobOperationType.Pack, JobOperationType.Ship];

        var byDepartment = departmentOrder
            .Select(type =>
            {
                var ops = openOps.Where(o => o.OperationType == type).ToList();
                return new CategoryValue(
                    DepartmentLabel(type),
                    Math.Round(ops.Sum(o => o.PlannedMinutes) / 60m, 1),
                    ops.Count);
            })
            .ToList();

        var byTask = openOps
            .GroupBy(o => new { o.OperationType, o.EquipmentId })
            .OrderBy(g => Array.IndexOf(departmentOrder, g.Key.OperationType))
            .ThenByDescending(g => g.Sum(o => o.PlannedMinutes))
            .Select(g => new WorkloadRow(
                TaskLabel(g.Key.OperationType, g.Key.EquipmentId, pressNames, finishingNames),
                DepartmentLabel(g.Key.OperationType),
                g.Count(),
                g.Select(o => o.JobId).Distinct().Count(),
                Math.Round(g.Sum(o => o.PlannedMinutes) / 60m, 1)))
            .ToList();

        return new WorkloadSection(
            Math.Round(openOps.Sum(o => o.PlannedMinutes) / 60m, 1),
            openOps.Count,
            openOps.Select(o => o.JobId).Distinct().Count(),
            byDepartment,
            byTask);
    }

    private static string DepartmentLabel(JobOperationType type) => type switch
    {
        JobOperationType.Press => "Press",
        JobOperationType.Finishing => "Finishing",
        JobOperationType.Inspection => "QC inspection",
        JobOperationType.Pack => "Pack",
        JobOperationType.Ship => "Ship",
        _ => type.ToString()
    };

    private static string TaskLabel(
        JobOperationType type,
        Guid? equipmentId,
        IReadOnlyDictionary<Guid, string> pressNames,
        IReadOnlyDictionary<Guid, string> finishingNames) => type switch
    {
        JobOperationType.Press when equipmentId is Guid pressId && pressNames.TryGetValue(pressId, out var press) => press,
        JobOperationType.Finishing when equipmentId is Guid finId && finishingNames.TryGetValue(finId, out var fin) => fin,
        _ => DepartmentLabel(type)
    };

    private async Task<SalesSection> LoadSalesAsync(
        DateTime fromDateTime, int rangeDays, DateOnly today, CancellationToken ct)
    {
        var openStatuses = new[] { SalesOrderStatus.Open, SalesOrderStatus.InProduction };
        var openOrders = await db.SalesOrders
            .Where(o => openStatuses.Contains(o.Status))
            .Select(o => new { o.Status, Value = o.Lines.Sum(l => (decimal?)l.LineTotal) ?? 0 })
            .ToListAsync(ct);

        var intakeRaw = await db.SalesOrders
            .Where(o => o.Status != SalesOrderStatus.Cancelled && o.OrderedAt >= fromDateTime)
            .Select(o => new { o.OrderedAt, Value = o.Lines.Sum(l => (decimal?)l.LineTotal) ?? 0 })
            .ToListAsync(ct);

        var recent = await db.SalesOrders
            .OrderByDescending(o => o.OrderedAt)
            .Take(8)
            .Select(o => new RecentOrderItem(
                o.Id,
                o.OrderNumber,
                o.Customer.Name,
                o.Status,
                o.OrderedAt,
                o.Lines.Sum(l => (decimal?)l.LineTotal) ?? 0))
            .ToListAsync(ct);

        return new SalesSection(
            openOrders.Count(o => o.Status == SalesOrderStatus.Open),
            openOrders.Sum(o => o.Value),
            openOrders.Count(o => o.Status == SalesOrderStatus.InProduction),
            BucketByTime(intakeRaw.Select(x => (DateOnly.FromDateTime(x.OrderedAt), x.Value)), rangeDays, today),
            recent);
    }

    private async Task<JobsSection> LoadJobsAsync(DateOnly today, CancellationToken ct)
    {
        var pipelineRaw = await db.Jobs
            .Where(j => WipStatuses.Contains(j.Status))
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var pipeline = WipStatuses
            .Select(s => new CategoryValue(
                JobStatusLabel(s),
                pipelineRaw.FirstOrDefault(p => p.Status == s)?.Count ?? 0,
                pipelineRaw.FirstOrDefault(p => p.Status == s)?.Count ?? 0))
            .ToList();

        var overdueCount = await db.Jobs
            .CountAsync(j => WipStatuses.Contains(j.Status) && j.DueDate != null && j.DueDate < today, ct);

        var dueSoon = await db.Jobs
            .Where(j => WipStatuses.Contains(j.Status) && j.DueDate != null)
            .OrderBy(j => j.DueDate)
            .ThenBy(j => j.Priority)
            .Take(8)
            .Select(j => new JobDueItem(
                j.Id,
                j.JobNumber,
                j.Product.Description,
                j.SalesOrderLine.SalesOrder.Customer.Name,
                j.Status,
                j.DueDate!.Value,
                j.DueDate < today))
            .ToListAsync(ct);

        return new JobsSection(pipeline.Sum(p => p.Count), overdueCount, pipeline, dueSoon);
    }

    private async Task<EstimatesSection> LoadEstimatesAsync(DateTime fromDateTime, CancellationToken ct)
    {
        var funnelRaw = await db.Estimates
            .Where(e => e.CreatedAt >= fromDateTime)
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var openCount = await db.Estimates
            .CountAsync(e => e.Status == EstimateStatus.Draft || e.Status == EstimateStatus.Sent, ct);

        EstimateStatus[] order =
            [EstimateStatus.Draft, EstimateStatus.Sent, EstimateStatus.Won, EstimateStatus.Lost, EstimateStatus.Expired];
        var funnel = order
            .Select(s =>
            {
                var count = funnelRaw.FirstOrDefault(f => f.Status == s)?.Count ?? 0;
                return new CategoryValue(s.ToString(), count, count);
            })
            .ToList();

        var won = funnelRaw.FirstOrDefault(f => f.Status == EstimateStatus.Won)?.Count ?? 0;
        var lost = funnelRaw.FirstOrDefault(f => f.Status == EstimateStatus.Lost)?.Count ?? 0;
        decimal? winRate = won + lost > 0 ? Math.Round(100m * won / (won + lost), 1) : null;

        return new EstimatesSection(openCount, won, lost, winRate, funnel);
    }

    private async Task<FinanceSection> LoadFinanceAsync(
        DateOnly fromDate, int rangeDays, DateOnly today, CancellationToken ct)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);

        var revenueMtd = await db.Invoices
            .Where(i => i.Status != InvoiceStatus.Void && i.InvoiceDate >= monthStart)
            .SumAsync(i => (decimal?)i.Total, ct) ?? 0;

        var revenueLastMonth = await db.Invoices
            .Where(i => i.Status != InvoiceStatus.Void && i.InvoiceDate >= lastMonthStart && i.InvoiceDate < monthStart)
            .SumAsync(i => (decimal?)i.Total, ct) ?? 0;

        var openArStatuses = new[] { InvoiceStatus.Sent, InvoiceStatus.PartiallyPaid };
        var openInvoices = await db.Invoices
            .Where(i => openArStatuses.Contains(i.Status) && i.BalanceDue > 0)
            .Select(i => new { i.DueDate, i.BalanceDue })
            .ToListAsync(ct);

        var aging = new[]
        {
            new CategoryValue("Current", openInvoices.Where(i => i.DueDate >= today).Sum(i => i.BalanceDue),
                openInvoices.Count(i => i.DueDate >= today)),
            AgingBucket(openInvoices.Select(i => (i.DueDate, i.BalanceDue)), today, 1, 30, "1–30 days"),
            AgingBucket(openInvoices.Select(i => (i.DueDate, i.BalanceDue)), today, 31, 60, "31–60 days"),
            AgingBucket(openInvoices.Select(i => (i.DueDate, i.BalanceDue)), today, 61, 90, "61–90 days"),
            AgingBucket(openInvoices.Select(i => (i.DueDate, i.BalanceDue)), today, 91, int.MaxValue, "90+ days"),
        };

        var trendRaw = await db.Invoices
            .Where(i => i.Status != InvoiceStatus.Void && i.InvoiceDate >= fromDate)
            .Select(i => new { i.InvoiceDate, i.Total })
            .ToListAsync(ct);

        var topCustomers = (await db.Invoices
                .Where(i => i.Status != InvoiceStatus.Void && i.InvoiceDate >= fromDate)
                .GroupBy(i => i.Customer.Name)
                .Select(g => new { Name = g.Key, Value = g.Sum(i => i.Total), Count = g.Count() })
                .OrderByDescending(g => g.Value)
                .Take(8)
                .ToListAsync(ct))
            .Select(g => new CategoryValue(g.Name, g.Value, g.Count))
            .ToList();

        return new FinanceSection(
            revenueMtd,
            revenueLastMonth,
            openInvoices.Sum(i => i.BalanceDue),
            openInvoices.Count,
            BucketByTime(trendRaw.Select(x => (x.InvoiceDate, x.Total)), rangeDays, today),
            aging,
            topCustomers);
    }

    private async Task<InventorySection> LoadInventoryAsync(CancellationToken ct)
    {
        var rollStatuses = new[] { RollStatus.Available, RollStatus.Staged };
        var stockLevelsRaw = await db.Rolls
            .Where(r => rollStatuses.Contains(r.Status) && r.RemainingLengthLf > 0)
            .GroupBy(r => new { r.Stock.Code, r.Stock.Description, r.Stock.MinOrderQtyLf })
            .Select(g => new
            {
                g.Key.Code,
                g.Key.Description,
                g.Key.MinOrderQtyLf,
                RollCount = g.Count(),
                RemainingLf = g.Sum(r => r.RemainingLengthLf)
            })
            .ToListAsync(ct);

        var stockLevels = stockLevelsRaw
            .Select(s => new StockLevelRow(
                s.Code,
                s.Description,
                s.RollCount,
                Math.Round(s.RemainingLf, 0),
                s.MinOrderQtyLf,
                s.MinOrderQtyLf > 0 && s.RemainingLf < s.MinOrderQtyLf))
            .OrderByDescending(s => s.RemainingLf)
            .ToList();

        var openPoStatuses = new[] { PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived };
        var openPos = await db.PurchaseOrders
            .Where(p => openPoStatuses.Contains(p.Status))
            .Select(p => new { Value = p.Lines.Sum(l => (decimal?)l.LineTotal) ?? 0 })
            .ToListAsync(ct);

        return new InventorySection(
            stockLevelsRaw.Sum(s => s.RollCount),
            Math.Round(stockLevelsRaw.Sum(s => s.RemainingLf), 0),
            stockLevels.Count(s => s.Low),
            openPos.Count,
            openPos.Sum(p => p.Value),
            stockLevels.Take(12).ToList());
    }

    private async Task<ShippingSection> LoadShippingAsync(DateOnly today, CancellationToken ct)
    {
        var weekAgo = today.AddDays(-7);
        var pending = await db.Shipments.CountAsync(s => s.Status == ShipmentStatus.Pending, ct);
        var inTransit = await db.Shipments.CountAsync(s => s.Status == ShipmentStatus.InTransit, ct);
        var shippedLast7 = await db.Shipments
            .CountAsync(s => s.Status != ShipmentStatus.Pending && s.ShipDate >= weekAgo, ct);
        return new ShippingSection(pending, inTransit, shippedLast7);
    }

    private static CategoryValue AgingBucket(
        IEnumerable<(DateOnly DueDate, decimal BalanceDue)> invoices,
        DateOnly today, int fromDays, int toDays, string label)
    {
        var inBucket = invoices
            .Where(i =>
            {
                var overdue = today.DayNumber - i.DueDate.DayNumber;
                return overdue >= fromDays && overdue <= toDays;
            })
            .ToList();
        return new CategoryValue(label, inBucket.Sum(i => i.BalanceDue), inBucket.Count);
    }

    /// <summary>Buckets values weekly for ranges up to ~3 months, monthly beyond, padding empty buckets.</summary>
    private static List<TimePoint> BucketByTime(
        IEnumerable<(DateOnly Date, decimal Value)> values, int rangeDays, DateOnly today)
    {
        var items = values.ToList();
        var result = new List<TimePoint>();

        if (rangeDays <= 95)
        {
            var weekCount = (int)Math.Ceiling(rangeDays / 7.0);
            var start = today.AddDays(-7 * (weekCount - 1));
            // Align to the Monday of the starting week.
            start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
            for (var ws = start; ws <= today; ws = ws.AddDays(7))
            {
                var we = ws.AddDays(7);
                result.Add(new TimePoint(
                    ws.ToString("MMM d"),
                    items.Where(i => i.Date >= ws && i.Date < we).Sum(i => i.Value)));
            }
        }
        else
        {
            var monthCount = (int)Math.Round(rangeDays / 30.4);
            var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-(monthCount - 1));
            for (var ms = start; ms <= today; ms = ms.AddMonths(1))
            {
                var me = ms.AddMonths(1);
                result.Add(new TimePoint(
                    ms.ToString("MMM yy"),
                    items.Where(i => i.Date >= ms && i.Date < me).Sum(i => i.Value)));
            }
        }

        return result;
    }

    private static string JobStatusLabel(JobStatus status) => status switch
    {
        JobStatus.PrePress => "Pre-press",
        JobStatus.Queued => "Queued",
        JobStatus.Printed => "Printed",
        JobStatus.Finished => "Finished",
        JobStatus.Rewound => "Rewound",
        _ => status.ToString()
    };
}
