using System.Text.Json;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Domain.Jobs;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.Rolls;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Jobs;

public record JobListItem(
    Guid Id,
    string JobNumber,
    string CustomerName,
    string ProductDescription,
    JobStatus Status,
    DateOnly? DueDate,
    DateOnly? ScheduledForDate,
    int Priority,
    int QuantityOrdered,
    Guid SalesOrderId,
    string OrderNumber,
    bool IsOutsourced = false,
    /// <summary>An imposed PDF exists for the job (pre-press has run the imposition).</summary>
    bool IsImposed = false,
    /// <summary>The imposed PDF predates the product's current artwork.</summary>
    bool ImposedIsStale = false);

/// <summary>Vendor facts for an outsourced job, shown on the job page and ticket instead of press info.</summary>
public record JobOutsourceInfo(
    Guid OutsourcedItemId,
    string? VendorName,
    string? QuoteNumber,
    decimal? VendorCost,
    DateOnly? ExpectedIn,
    DateTime? SentToVendorAt,
    DateTime? ReceivedAt,
    int QuantityReceived,
    string? PrivateNotes);

/// <summary>A job on the same sales order, for cross-referencing sibling lines.</summary>
public record OrderJobLink(
    Guid JobId,
    string JobNumber,
    int LineNumber,
    string ProductDescription,
    JobStatus Status,
    int QuantityOrdered);

public record JobCostSummary(
    decimal? EstimatedCost,
    JobActualCostResult ActualCost);

public record JobDetail(
    Job Job,
    string CustomerName,
    string ProductDescription,
    string? ArtworkFilePath,
    string? ArtworkOriginalFileName,
    Stock Substrate,
    JobCostSummary CostSummary,
    IReadOnlyList<(JobOperation Operation, string? EquipmentName, string TypeLabel)> Operations,
    Guid SalesOrderId,
    string OrderNumber,
    string? OrderNotes,
    string? LineNotes,
    JobOutsourceInfo? Outsource = null);

public record JobTicketRouteStep(
    int Sequence,
    JobOperationType Type,
    string Description,
    decimal PlannedMinutes);

public record JobTicketDetail(
    Job Job,
    string CustomerName,
    string ProductDescription,
    string ProductSku,
    string OrderNumber,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    string SubstrateDescription,
    InkSet InkSet,
    string? SpecialInksSummary,
    string? DieDescription,
    string? ScheduledPressName,
    RollSpec? RollSpec,
    IReadOnlyList<JobTicketRouteStep> Route,
    string? OrderNotes = null,
    string? LineNotes = null,
    IReadOnlyList<string>? OrderCharges = null,
    Die? Die = null,
    string? ShippingMethodName = null,
    JobOutsourceInfo? Outsource = null);

public record OperatorJobView(
    Job Job,
    string CustomerName,
    string ProductDescription,
    JobOperation? CurrentOperation,
    bool IsClockedOn,
    Guid? ScannedRollId,
    string? ScannedRollBarcode);

public record ScheduleJobInput(DateOnly ScheduledForDate, Guid? PressId);

public record FinishingTaskView(
    Guid OperationId,
    string Label,
    JobOperationStatus Status,
    decimal PlannedMinutes,
    decimal ActualMinutes,
    bool IsLamination = false,
    Guid? MaterialStockId = null)
{
    public bool IsDone => Status is JobOperationStatus.Complete or JobOperationStatus.Skipped;
}

/// <summary>A finishing task row inside the job action popup, with its material claim context.</summary>
public record JobActionTask(
    Guid OperationId,
    string Label,
    JobOperationStatus Status,
    decimal PlannedMinutes,
    decimal ActualMinutes,
    bool IsLamination,
    string? MaterialLabel,
    Guid? MaterialStockId)
{
    public bool IsDone => Status is JobOperationStatus.Complete or JobOperationStatus.Skipped;
}

/// <summary>Prefilled counts/time for the stage operation the action popup records.</summary>
public record JobActionCounts(
    Guid OperationId,
    string Label,
    int Good,
    int Waste,
    decimal Downtime,
    DowntimeReasonCode? Reason,
    decimal PlannedMinutes,
    decimal ActualMinutes,
    bool HasRecorded);

/// <summary>Everything the job action popup needs to render the stage-appropriate form.</summary>
public record JobActionPanel(
    Guid JobId,
    string JobNumber,
    string CustomerName,
    string ProductDescription,
    JobStatus Status,
    JobStatus? NextStatus,
    string? NextLabel,
    int QuantityOrdered,
    string SubstrateLabel,
    Guid SubstrateStockId,
    bool ShowRollClaim,
    JobActionCounts? Counts,
    IReadOnlyList<JobActionTask> FinishingTasks,
    IReadOnlyList<RollPickerOption> Rolls);

public class JobService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    EstimateCalculationMapper calculationMapper,
    EstimatingService estimatingService,
    RollService rollService)
{
    /// <summary>Job statuses considered "live" — still moving through production.</summary>
    public static readonly IReadOnlyList<JobStatus> LiveStatuses =
    [
        JobStatus.Outsourced, JobStatus.PrePress, JobStatus.Queued, JobStatus.Printed, JobStatus.Finished, JobStatus.Rewound
    ];

    /// <summary>The next production status and its action label for a job in the given status,
    /// or null if there is none.</summary>
    public static (JobStatus Next, string Label)? NextStep(JobStatus status) => status switch
    {
        JobStatus.PrePress => (JobStatus.Queued, "Send to press"),
        JobStatus.Queued => (JobStatus.Printed, "Mark printed"),
        JobStatus.Printed => (JobStatus.Finished, "Mark finished"),
        JobStatus.Finished => (JobStatus.Rewound, "Mark rewound"),
        JobStatus.Rewound => (JobStatus.Shipped, "Mark shipped"),
        JobStatus.Shipped => (JobStatus.Closed, "Close job"),
        _ => null
    };

    public async Task<PagedResult<JobListItem>> ListAsync(
        string? search,
        JobStatus? status,
        Guid? pressId,
        Guid? customerId,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        DateOnly? scheduledDate,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<JobStatus>? includeStatuses = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Jobs.AsNoTracking()
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder)
            .AsQueryable();

        if (includeStatuses is { Count: > 0 })
        {
            query = query.Where(j => includeStatuses.Contains(j.Status));
        }
        else if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        if (pressId.HasValue)
        {
            query = query.Where(j => j.ScheduledPressId == pressId.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(j => j.Product.CustomerAssignments.Any(a => a.CustomerId == customerId.Value));
        }

        if (dueFrom.HasValue)
        {
            query = query.Where(j => j.DueDate >= dueFrom.Value);
        }

        if (dueTo.HasValue)
        {
            query = query.Where(j => j.DueDate <= dueTo.Value);
        }

        if (scheduledDate.HasValue)
        {
            query = query.Where(j => j.ScheduledForDate == scheduledDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(j =>
                j.JobNumber.ToUpper().Contains(term)
                || j.Product.Description.ToUpper().Contains(term)
                || (j.SalesOrderLine.Description != null && j.SalesOrderLine.Description.ToUpper().Contains(term))
                || (j.Product.PrimaryCustomer != null && j.Product.PrimaryCustomer.Name.ToUpper().Contains(term)));
        }

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "number" => query.OrderByDir(desc, j => j.JobNumber),
            "order" => query.OrderByDir(desc, j => j.SalesOrderLine.SalesOrder.OrderNumber),
            "customer" => query.OrderByDir(desc, j => j.Product.PrimaryCustomer != null ? j.Product.PrimaryCustomer.Name : ""),
            "product" => query.OrderByDir(desc, j => j.SalesOrderLine.Description ?? j.Product.Description),
            "status" => query.OrderByDir(desc, j => j.Status).ThenByDescending(j => j.CreatedAt),
            "due" => query.OrderByDir(desc, j => j.DueDate).ThenBy(j => j.Priority),
            "scheduled" => query.OrderByDir(desc, j => j.ScheduledForDate),
            "qty" => query.OrderByDir(desc, j => j.QuantityOrdered),
            "priority" => query.OrderByDir(desc, j => j.Priority).ThenBy(j => j.DueDate),
            _ => query.OrderBy(j => j.Priority).ThenBy(j => j.DueDate)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobListItem(
                j.Id,
                j.JobNumber,
                j.Product.PrimaryCustomer != null ? j.Product.PrimaryCustomer.Name : "",
                j.SalesOrderLine.Description ?? j.Product.Description,
                j.Status,
                j.DueDate,
                j.ScheduledForDate,
                j.Priority,
                j.QuantityOrdered,
                j.SalesOrderLine.SalesOrderId,
                j.SalesOrderLine.SalesOrder.OrderNumber,
                j.IsOutsourced,
                j.ImposedArtworkFilePath != null,
                j.ImposedArtworkFilePath != null && !j.ImposedIsManual && j.ImposedFromArtworkFilePath != j.Product.ArtworkFilePath))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobListItem>(items, page, pageSize, total);
    }

    /// <summary>All jobs on the same sales order as the given job (including it), in line order.</summary>
    public async Task<IReadOnlyList<OrderJobLink>> GetOrderJobsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var salesOrderId = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.SalesOrderLine.SalesOrderId)
            .FirstOrDefaultAsync(cancellationToken);
        if (salesOrderId == Guid.Empty)
        {
            return [];
        }

        return await db.Jobs.AsNoTracking()
            .Where(j => j.SalesOrderLine.SalesOrderId == salesOrderId)
            .OrderBy(j => j.SalesOrderLine.LineNumber)
            .Select(j => new OrderJobLink(
                j.Id,
                j.JobNumber,
                j.SalesOrderLine.LineNumber,
                j.SalesOrderLine.Description ?? j.Product.Description,
                j.Status,
                j.QuantityOrdered))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Ticket details for every job on the printed job's sales order, in line order.</summary>
    public async Task<IReadOnlyList<JobTicketDetail>> GetOrderTicketDetailsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var links = await GetOrderJobsAsync(jobId, cancellationToken);
        var details = new List<JobTicketDetail>(links.Count);
        foreach (var link in links)
        {
            if (await GetTicketDetailAsync(link.JobId, cancellationToken) is { } detail)
            {
                details.Add(detail);
            }
        }

        return details;
    }

    public async Task<JobDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.Product).ThenInclude(p => p.Substrate)
            .Include(j => j.Operations).ThenInclude(o => o.TimeEntries)
            .Include(j => j.MaterialUsages).ThenInclude(m => m.Stock)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.OutsourcedItem).ThenInclude(o => o!.Vendor)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.OutsourcedItem).ThenInclude(o => o!.Receipts)
            .AsSplitQuery()
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return null;
        }

        var presses = await db.Presses.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var finishing = await db.FinishingOperations.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var operations = job.Operations
            .OrderBy(o => o.Sequence)
            .Select(o => (o, GetEquipmentName(o, presses, finishing), GetOperationTypeLabel(o, finishing)))
            .ToList();

        var actualCost = await CalculateActualCostAsync(job, cancellationToken);
        var estimatedCost = await GetEstimatedCostAsync(job, cancellationToken);

        var order = job.SalesOrderLine.SalesOrder;
        return new JobDetail(
            job,
            job.Product.PrimaryCustomer != null ? job.Product.PrimaryCustomer.Name : "",
            job.SalesOrderLine.Description ?? job.Product.Description,
            job.Product.ArtworkFilePath,
            job.Product.ArtworkOriginalFileName,
            job.Product.Substrate,
            new JobCostSummary(estimatedCost, actualCost),
            operations,
            order.Id,
            order.OrderNumber,
            order.Notes,
            job.SalesOrderLine.LineNotes,
            ToOutsourceInfo(job.SalesOrderLine.OutsourcedItem));
    }

    private static JobOutsourceInfo? ToOutsourceInfo(OutsourcedItem? item) => item is null
        ? null
        : new JobOutsourceInfo(
            item.Id,
            item.Vendor?.Name,
            item.QuoteNumber,
            item.VendorCost,
            item.ExpectedIn,
            item.SentToVendorAt,
            item.ReceivedAt,
            item.QuantityReceived,
            item.PrivateNotes);

    /// <summary>Updates the parent sales order's header notes from the job page (shared notes).</summary>
    public async Task UpdateOrderNotesAsync(Guid jobId, string? notes, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder)
            .SingleAsync(j => j.Id == jobId, cancellationToken);
        job.SalesOrderLine.SalesOrder.UpdateNotes(notes, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<JobTicketDetail?> GetTicketDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs.AsNoTracking()
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.Product).ThenInclude(p => p.Substrate)
            .Include(j => j.Product).ThenInclude(p => p.RollSpec)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder).ThenInclude(o => o.ShippingMethod)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.OutsourcedItem).ThenInclude(o => o!.Vendor)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.OutsourcedItem).ThenInclude(o => o!.Receipts)
            .Include(j => j.Operations)
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return null;
        }

        var finishing = await db.FinishingOperations.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        // Material stock assigned per finishing operation (lamination/foil), snapshotted on the job spec.
        var materialStockByOperation = EstimateCalculationMapper
            .DeserializeFinishingOperations(job.Spec?.FinishingOperationsJson ?? "[]")
            .Where(f => f.StockId is { } sid && sid != Guid.Empty)
            .GroupBy(f => f.OperationId)
            .ToDictionary(g => g.Key, g => g.First().StockId!.Value);
        var materialStockIds = materialStockByOperation.Values.Distinct().ToList();
        var materialStockLabels = await db.Stocks.AsNoTracking()
            .Where(s => materialStockIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => $"{s.Code} — {s.Description}", cancellationToken);

        var route = job.Operations.OrderBy(o => o.Sequence).Select(o =>
        {
            var description = o.OperationType switch
            {
                JobOperationType.Press => "Press",
                JobOperationType.Finishing when o.EquipmentId.HasValue && finishing.TryGetValue(o.EquipmentId.Value, out var fin)
                    => fin.Description,
                JobOperationType.Inspection => "QC inspection",
                JobOperationType.Pack => "Pack",
                JobOperationType.Ship => "Ship",
                _ => o.OperationType.ToString()
            };
            if (o.OperationType == JobOperationType.Finishing
                && o.EquipmentId is { } equipmentId
                && materialStockByOperation.TryGetValue(equipmentId, out var materialId)
                && materialStockLabels.TryGetValue(materialId, out var materialLabel))
            {
                description += $" — material: {materialLabel}";
            }
            return new JobTicketRouteStep(o.Sequence, o.OperationType, description, o.PlannedMinutes);
        }).ToList();

        // The job spec is the ordered snapshot; fall back to the product template for
        // pre-refactor jobs whose spec hasn't been backfilled.
        var spec = job.Spec ?? job.Product.ToLabelSpec();

        var substrateDescription = await db.Stocks.AsNoTracking()
            .Where(s => s.Id == spec.SubstrateId)
            .Select(s => $"{s.Code} — {s.Description}")
            .FirstOrDefaultAsync(cancellationToken)
            ?? job.Product.Substrate.Description;

        Die? die = null;
        if (spec.DieId is { } dieId)
        {
            die = await db.Dies.AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.Supplier)
                .FirstOrDefaultAsync(d => d.Id == dieId, cancellationToken);
        }

        var inkParts = new List<string>();
        if (spec.WhiteHits > 0)
        {
            inkParts.Add($"White ×{spec.WhiteHits} ({spec.WhiteCoveragePct * 100:0}%)");
        }
        var spots = EstimateCalculationMapper.DeserializeSpots(spec.SpotsJson);
        if (spots.Count > 0)
        {
            var spotInkIds = spots.Select(s => s.InkId).ToList();
            var spotCodes = await db.Inks.AsNoTracking()
                .Where(i => spotInkIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Code, cancellationToken);
            inkParts.AddRange(spots.OrderBy(s => s.SortOrder).Select(s =>
                $"{spotCodes.GetValueOrDefault(s.InkId, "Spot")} ×{s.Hits} ({s.CoveragePct * 100:0}%)"));
        }

        string? scheduledPressName = null;
        if (job.ScheduledPressId is { } pressId)
        {
            scheduledPressName = await db.Presses.AsNoTracking()
                .Where(p => p.Id == pressId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var order = job.SalesOrderLine.SalesOrder;

        // Non-label charges on the order (die creation, design) — surfaced on the ticket so the
        // floor knows e.g. a new die is being made, without a job existing for it.
        var orderCharges = await db.SalesOrderCharges.AsNoTracking()
            .Where(c => c.SalesOrderId == order.Id)
            .OrderBy(c => c.LineNumber)
            .Select(c => new { c.Description, c.Quantity })
            .ToListAsync(cancellationToken);

        return new JobTicketDetail(
            job,
            job.Product.PrimaryCustomer != null ? job.Product.PrimaryCustomer.Name : "",
            job.SalesOrderLine.Description ?? job.Product.Description,
            job.Product.InternalSku,
            order.OrderNumber,
            order.CustomerPoNumber,
            order.RequestedShipDate,
            spec.LabelAcrossIn,
            spec.LabelAroundIn,
            substrateDescription,
            spec.InkSet,
            inkParts.Count > 0 ? string.Join(", ", inkParts) : null,
            die?.Description,
            scheduledPressName,
            job.Product.RollSpec,
            route,
            order.Notes,
            job.SalesOrderLine.LineNotes,
            orderCharges.Select(c => c.Quantity > 1 ? $"{c.Description} (×{c.Quantity})" : c.Description).ToList(),
            die,
            order.ShippingMethod?.Name,
            ToOutsourceInfo(job.SalesOrderLine.OutsourcedItem));
    }

    public async Task<OperatorJobView?> GetOperatorViewAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.SalesOrderLine)
            .Include(j => j.Operations).ThenInclude(o => o.TimeEntries)
            .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        return job is null ? null : await BuildOperatorView(job, cancellationToken);
    }

    public async Task<OperatorJobView?> GetOperatorViewByNumberAsync(
        string jobNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = jobNumber.Trim().ToUpperInvariant();
        var job = await db.Jobs
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.SalesOrderLine)
            .Include(j => j.Operations).ThenInclude(o => o.TimeEntries)
            .SingleOrDefaultAsync(j => j.JobNumber.ToUpper() == normalized, cancellationToken);

        return job is null ? null : await BuildOperatorView(job, cancellationToken);
    }

    private async Task<OperatorJobView> BuildOperatorView(Job job, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var current = job.Operations
            .OrderBy(o => o.Sequence)
            .FirstOrDefault(o => o.Status is JobOperationStatus.Pending or JobOperationStatus.InProgress);

        var isClockedOn = current?.TimeEntries.Any(t => t.UserId == userId && t.ClockedOutAt is null) == true;
        string? rollBarcode = null;
        if (current?.ScannedRollId is Guid rollId)
        {
            rollBarcode = await db.Rolls.AsNoTracking()
                .Where(r => r.Id == rollId)
                .Select(r => r.RollBarcode)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new OperatorJobView(
            job,
            job.Product.PrimaryCustomer != null ? job.Product.PrimaryCustomer.Name : "",
            job.SalesOrderLine.Description ?? job.Product.Description,
            current,
            isClockedOn,
            current?.ScannedRollId,
            rollBarcode);
    }

    public async Task<IReadOnlyList<Job>> ScheduleFromSalesOrderAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var order = await db.SalesOrders
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Lines).ThenInclude(l => l.OutsourcedItem)
            .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken)
            ?? throw new InvalidOperationException("Sales order not found.");

        if (order.Status is not SalesOrderStatus.Open)
        {
            throw new InvalidOperationException("Only open sales orders can be scheduled for production.");
        }

        var existingLineIds = await db.Jobs.AsNoTracking()
            .Where(j => order.Lines.Select(l => l.Id).Contains(j.SalesOrderLineId))
            .Select(j => j.SalesOrderLineId)
            .ToListAsync(cancellationToken);

        var created = new List<Job>();
        foreach (var line in order.Lines.OrderBy(l => l.LineNumber))
        {
            if (existingLineIds.Contains(line.Id))
            {
                continue;
            }

            var jobNumber = await documentNumbers.NextJobNumberAsync(cancellationToken);
            var quantityPlanned = (int)Math.Ceiling(line.Quantity * 1.05m);

            // Snapshot the ordered spec onto the job; seed from the product for pre-refactor lines
            // whose Spec hasn't been backfilled yet.
            var spec = line.Spec ?? line.Product.ToLabelSpec();

            var job = Job.CreatePlanned(
                Guid.NewGuid(),
                jobNumber,
                line.Id,
                line.ProductId,
                line.Quantity,
                quantityPlanned,
                order.RequestedShipDate,
                priority: 5,
                notes: line.LineNotes,
                spec,
                userId,
                now);

            // Outsourced lines skip press/finishing: the job waits at the vendor and only carries the
            // in-house receiving steps. If the vendor already delivered before release, it is ready to ship.
            var outsourced = line.OutsourcedItem is not null;
            if (outsourced)
            {
                job.MarkOutsourced(userId, now);
            }

            var operations = await BuildOperationsAsync(
                job.Id, spec, quantityPlanned, order.CustomerId, userId, now, cancellationToken, outsourced);
            foreach (var operation in operations)
            {
                job.AddOperation(operation);
                db.JobOperations.Add(operation);
            }

            if (outsourced && line.OutsourcedItem!.IsComplete)
            {
                job.ReceiveOutsourced(userId, now);
                CompletePassedOperations(job, job.Status, userId, now);
            }

            db.Jobs.Add(job);
            created.Add(job);
        }

        if (created.Count == 0)
        {
            throw new InvalidOperationException("Jobs already exist for all lines on this order.");
        }

        order.AdvanceStatus(SalesOrderStatus.InProduction, userId, now);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task ScheduleAsync(
        Guid jobId,
        ScheduleJobInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        job.Schedule(input.ScheduledForDate, input.PressId, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Records or adjusts an operation's production counts and time taken as absolute values.</summary>
    public async Task RecordCountsAsync(
        Guid operationId,
        int goodCount,
        int wasteCount,
        decimal downtimeMinutes,
        DowntimeReasonCode? downtimeReason,
        decimal actualMinutes,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var operation = await db.JobOperations.SingleAsync(o => o.Id == operationId, cancellationToken);
        operation.SetCounts(goodCount, wasteCount, downtimeMinutes, downtimeReason, actualMinutes, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClockOnAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var operation = await db.JobOperations
            .Include(o => o.TimeEntries)
            .Include(o => o.Job)
            .SingleAsync(o => o.Id == operationId, cancellationToken);

        operation.Start(userId, now, userId, now);
        var entry = operation.ClockOn(Guid.NewGuid(), userId, now);
        db.JobTimeEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClockOffAsync(
        Guid operationId,
        int goodCount,
        int wasteCount,
        decimal downtimeMinutes,
        DowntimeReasonCode? downtimeReason,
        decimal? consumedLf,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var operation = await db.JobOperations
            .Include(o => o.TimeEntries)
            .Include(o => o.Job).ThenInclude(j => j.MaterialUsages)
            .AsSplitQuery()
            .SingleAsync(o => o.Id == operationId, cancellationToken);

        operation.ClockOff(userId, now, goodCount, wasteCount, downtimeMinutes, downtimeReason, userId, now);

        if (consumedLf is > 0 && operation.ScannedRollId is Guid rollId)
        {
            var roll = await db.Rolls.SingleAsync(r => r.Id == rollId, cancellationToken);
            roll.Consume(consumedLf.Value, userId, now);
            var movement = RollMovement.Create(
                Guid.NewGuid(),
                rollId,
                RollMovementType.Consume,
                -consumedLf.Value,
                operation.JobId,
                now,
                null,
                userId,
                now);
            db.RollMovements.Add(movement);
            roll.AddMovement(movement);

            var usage = JobMaterialUsage.Create(
                Guid.NewGuid(),
                operation.JobId,
                roll.StockId,
                rollId,
                consumedLf.Value,
                now,
                null,
                userId,
                now);
            operation.Job.AddMaterialUsage(usage);
            db.JobMaterialUsages.Add(usage);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ScanRollAsync(Guid operationId, string barcode, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var normalized = barcode.Trim().ToUpperInvariant();
        var roll = await db.Rolls.SingleOrDefaultAsync(r => r.RollBarcode == normalized, cancellationToken)
            ?? throw new InvalidOperationException("Roll not found.");

        if (roll.Status is not RollStatus.Available and not RollStatus.Staged)
        {
            throw new InvalidOperationException("Roll is not available for use.");
        }

        var operation = await db.JobOperations.SingleAsync(o => o.Id == operationId, cancellationToken);
        operation.LinkRoll(roll.Id, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var operation = await db.JobOperations
            .Include(o => o.TimeEntries)
            .Include(o => o.Job)
            .SingleAsync(o => o.Id == operationId, cancellationToken);

        operation.Complete(userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The vendor delivered an outsourced order line: its job (if the order has been released) moves
    /// straight to ready-to-ship. No-op when there is no job yet — release will route it there.
    /// </summary>
    public async Task ReceiveOutsourcedJobAsync(Guid salesOrderLineId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs
            .Include(j => j.Operations)
            .SingleOrDefaultAsync(j => j.SalesOrderLineId == salesOrderLineId, cancellationToken);
        if (job is null || job.Status is not JobStatus.Outsourced)
        {
            return;
        }

        job.ReceiveOutsourced(userId, now);
        CompletePassedOperations(job, job.Status, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    // The job status by which each operation type is considered done (i.e. its production stage is
    // behind the job). Moving the job past a stage completes that stage's still-pending operations, so
    // the operations list stays in sync with the status stepper. Finishing ops are completed
    // individually in the operator panel, but are covered here too as a backstop.
    private static readonly IReadOnlyDictionary<JobOperationType, JobStatus> OperationCompletedByStatus =
        new Dictionary<JobOperationType, JobStatus>
        {
            [JobOperationType.Press] = JobStatus.Printed,
            [JobOperationType.Finishing] = JobStatus.Finished,
            [JobOperationType.Inspection] = JobStatus.Rewound,
            [JobOperationType.Pack] = JobStatus.Shipped,
            [JobOperationType.Ship] = JobStatus.Shipped,
        };

    /// <summary>Completes any still-pending operations whose stage the job has now moved past. Only
    /// touches Pending ops (they have no open time entries, so <see cref="JobOperation.Complete"/> is
    /// safe); an InProgress op is left for the operator who is actively on it.</summary>
    private static void CompletePassedOperations(Job job, JobStatus status, Guid userId, DateTime now)
    {
        foreach (var operation in job.Operations)
        {
            if (operation.Status != JobOperationStatus.Pending)
            {
                continue;
            }

            if (OperationCompletedByStatus.TryGetValue(operation.OperationType, out var doneBy) && doneBy <= status)
            {
                operation.Complete(userId, now);
            }
        }
    }

    public async Task SetJobStatusAsync(
        Guid jobId,
        JobStatus status,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs.Include(j => j.Operations).SingleAsync(j => j.Id == jobId, cancellationToken);
        job.SetStatus(status, userId, now);
        CompletePassedOperations(job, status, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Counts of jobs in each of the supplied statuses (for the production stage nav).</summary>
    public async Task<IReadOnlyDictionary<JobStatus, int>> GetStatusCountsAsync(
        IEnumerable<JobStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        var wanted = statuses.Distinct().ToList();
        var counts = await db.Jobs.AsNoTracking()
            .Where(j => wanted.Contains(j.Status))
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var result = wanted.ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0);

        // The Outsourced stage badge counts open outsourced items (order lines and charges alike),
        // not jobs — a charge never has a job, and a line's job only exists once the order is released.
        if (result.ContainsKey(JobStatus.Outsourced))
        {
            result[JobStatus.Outsourced] = await db.OutsourcedItems.AsNoTracking()
                .Where(o => o.ReceivedAt == null && Outsourcing.OutsourceService.TrackedOrderStatuses.Contains(o.SalesOrder.Status))
                .CountAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>Advances a job forward to the given status (used by the production stage pages).</summary>
    public async Task AdvanceJobStatusAsync(
        Guid jobId,
        JobStatus target,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs.Include(j => j.Operations).SingleAsync(j => j.Id == jobId, cancellationToken);
        job.AdvanceStatus(target, userId, now);
        CompletePassedOperations(job, target, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Builds the job action popup for a job's current stage: the finishing task list when the
    /// job is in Finishing, otherwise the stage operation's counts/time prefills, plus the roll
    /// claim context for stages that consume material.
    /// </summary>
    public async Task<JobActionPanel?> GetActionPanelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs.AsNoTracking()
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.Product).ThenInclude(p => p.Substrate)
            .Include(j => j.SalesOrderLine)
            .Include(j => j.Operations).ThenInclude(o => o.TimeEntries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var finishing = await db.FinishingOperations.AsNoTracking().ToDictionaryAsync(f => f.Id, cancellationToken);
        var next = NextStep(job.Status);

        // Finishing-task mode: Printed jobs complete their tasks one by one (the job advances
        // itself when the last one is done), so the popup lists tasks instead of one counts form.
        var tasks = new List<JobActionTask>();
        if (job.Status == JobStatus.Printed)
        {
            var materialByOperation = MaterialStockByFinishingOperation(job.Spec);
            var stockIds = materialByOperation.Values.Distinct().ToList();
            var stockLabels = await db.Stocks.AsNoTracking()
                .Where(s => stockIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => $"{s.Code} — {s.Description}", cancellationToken);

            tasks = job.Operations
                .Where(o => o.OperationType == JobOperationType.Finishing)
                .OrderBy(o => o.Sequence)
                .Select(o =>
                {
                    Guid? materialStockId = o.EquipmentId is Guid eid && materialByOperation.TryGetValue(eid, out var mid)
                        ? mid
                        : null;
                    return new JobActionTask(
                        o.Id,
                        GetOperationTypeLabel(o, finishing),
                        o.Status,
                        o.PlannedMinutes,
                        o.ActualMinutes,
                        o.EquipmentId is Guid finId
                            && finishing.TryGetValue(finId, out var fin)
                            && fin.OperationType == FinishingOperationType.Laminate,
                        materialStockId is { } stockId ? stockLabels.GetValueOrDefault(stockId) : null,
                        materialStockId);
                })
                .ToList();
        }

        // Counts mode: the operation belonging to the job's current stage, prefilled with the
        // recorded values or the expected defaults (mirrors the jobs/{id} counts recorder).
        JobActionCounts? counts = null;
        if (job.Status != JobStatus.PrePress && tasks.Count == 0 && next is not null)
        {
            var operations = job.Operations.OrderBy(o => o.Sequence).ToList();
            var stageOperation = job.Status switch
            {
                JobStatus.Queued => operations.FirstOrDefault(o => o.OperationType == JobOperationType.Press),
                JobStatus.Finished => operations.FirstOrDefault(o => o.OperationType == JobOperationType.Inspection),
                JobStatus.Rewound => operations.FirstOrDefault(o => o.OperationType == JobOperationType.Pack)
                    ?? operations.FirstOrDefault(o => o.OperationType == JobOperationType.Ship),
                _ => null
            } ?? operations.FirstOrDefault(o => o.Status is JobOperationStatus.Pending or JobOperationStatus.InProgress);

            if (stageOperation is not null)
            {
                var presses = await db.Presses.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
                var hasRecorded = stageOperation.GoodCount > 0 || stageOperation.WasteCount > 0 || stageOperation.DowntimeMinutes > 0;
                var expectedWaste = (int)Math.Ceiling(job.QuantityPlanned * (job.Spec?.RunningWastePct ?? 0m));
                var clockedMinutes = Math.Round(stageOperation.TimeEntries
                    .Where(t => t.ClockedOutAt.HasValue)
                    .Sum(t => t.DurationHours) * 60m, 0);

                counts = new JobActionCounts(
                    stageOperation.Id,
                    $"{GetOperationTypeLabel(stageOperation, finishing)}{(GetEquipmentName(stageOperation, presses, finishing) is { } eq ? $" — {eq}" : "")}",
                    hasRecorded ? stageOperation.GoodCount : job.QuantityPlanned,
                    hasRecorded ? stageOperation.WasteCount : expectedWaste,
                    stageOperation.DowntimeMinutes,
                    stageOperation.DowntimeReasonCode,
                    stageOperation.PlannedMinutes,
                    stageOperation.ActualMinutes > 0
                        ? stageOperation.ActualMinutes
                        : clockedMinutes > 0 ? clockedMinutes : stageOperation.PlannedMinutes,
                    hasRecorded);
            }
        }

        // The substrate the job was ordered on (spec) — falling back to the product's default
        // for pre-spec jobs — heads the roll picker; the picker itself lists all consumable rolls.
        var substrateId = job.Spec?.SubstrateId is { } specSubstrateId && specSubstrateId != Guid.Empty
            ? specSubstrateId
            : job.Product.SubstrateId;
        var substrateLabel = substrateId == job.Product.SubstrateId
            ? $"{job.Product.Substrate.Code} — {job.Product.Substrate.Description}"
            : await db.Stocks.AsNoTracking()
                .Where(s => s.Id == substrateId)
                .Select(s => $"{s.Code} — {s.Description}")
                .FirstOrDefaultAsync(cancellationToken)
                ?? $"{job.Product.Substrate.Code} — {job.Product.Substrate.Description}";

        var showRollClaim = job.Status == JobStatus.Queued;
        var rolls = showRollClaim || tasks.Any(t => t.IsLamination && !t.IsDone)
            ? await rollService.ListPickerOptionsAsync(cancellationToken)
            : [];

        return new JobActionPanel(
            job.Id,
            job.JobNumber,
            job.Product.PrimaryCustomer?.Name ?? "",
            job.SalesOrderLine.Description ?? job.Product.Description,
            job.Status,
            next?.Next,
            next?.Label,
            job.QuantityOrdered,
            substrateLabel,
            substrateId,
            showRollClaim,
            counts,
            tasks,
            rolls);
    }

    /// <summary>
    /// The material stock each finishing operation on the job's spec calls for (e.g. the
    /// laminate for a laminate step), keyed by finishing operation id. Empty when the spec
    /// names none.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Guid> MaterialStockByFinishingOperation(LabelSpec? spec) =>
        EstimateCalculationMapper
            .DeserializeFinishingOperations(spec?.FinishingOperationsJson ?? "[]")
            .Where(f => f.StockId is { } sid && sid != Guid.Empty)
            .GroupBy(f => f.OperationId)
            .ToDictionary(g => g.Key, g => g.First().StockId!.Value);

    /// <summary>
    /// The action popup's submit: claims roll usage when entered, records the stage operation's
    /// counts/time, then advances the job to its next status.
    /// </summary>
    public async Task RecordAndAdvanceAsync(
        Guid jobId,
        Guid operationId,
        int goodCount,
        int wasteCount,
        decimal downtimeMinutes,
        DowntimeReasonCode? downtimeReason,
        decimal actualMinutes,
        string? rollBarcode,
        decimal? consumedLf,
        CancellationToken cancellationToken = default)
    {
        // A half-filled claim should error (inside RecordRollUsageAsync), not silently skip.
        if (!string.IsNullOrWhiteSpace(rollBarcode) || consumedLf is > 0)
        {
            await RecordRollUsageAsync(jobId, rollBarcode ?? string.Empty, consumedLf ?? 0m, cancellationToken);
        }

        if (operationId != Guid.Empty)
        {
            await RecordCountsAsync(
                operationId, goodCount, wasteCount, downtimeMinutes, downtimeReason, actualMinutes, cancellationToken);
        }

        var status = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.Status)
            .SingleAsync(cancellationToken);
        if (NextStep(status) is { } step)
        {
            await AdvanceJobStatusAsync(jobId, step.Next, cancellationToken);
        }
    }

    /// <summary>
    /// Records material consumed from a roll against a job — the operator scans the roll and enters
    /// the linear feet used during the press run. Updates roll inventory and the job's material usage.
    /// </summary>
    public async Task RecordRollUsageAsync(
        Guid jobId,
        string barcode,
        decimal consumedLf,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        if (consumedLf <= 0)
        {
            throw new InvalidOperationException("Enter how many linear feet were used.");
        }
        if (string.IsNullOrWhiteSpace(barcode))
        {
            throw new InvalidOperationException("Select or scan a roll.");
        }

        var normalized = barcode.Trim().ToUpperInvariant();
        var roll = await db.Rolls.SingleOrDefaultAsync(r => r.RollBarcode == normalized, cancellationToken)
            ?? throw new InvalidOperationException("Roll not found.");

        if (!await db.Jobs.AnyAsync(j => j.Id == jobId, cancellationToken))
        {
            throw new InvalidOperationException("Job not found.");
        }

        roll.Consume(consumedLf, userId, now);

        var movement = RollMovement.Create(
            Guid.NewGuid(), roll.Id, RollMovementType.Consume, -consumedLf, jobId, now, null, userId, now);
        db.RollMovements.Add(movement);
        roll.AddMovement(movement);

        var usage = JobMaterialUsage.Create(
            Guid.NewGuid(), jobId, roll.StockId, roll.Id, consumedLf, now, null, userId, now);
        db.JobMaterialUsages.Add(usage);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Marks a finishing task complete, recording how long it took (an entry under the planned
    /// minutes reports a favorable variance) and any material claimed from a roll (lamination).
    /// When the last finishing task on a Printed job is done, the job auto-advances to Finished
    /// so it moves on to the Rewinding stage.
    /// </summary>
    public async Task CompleteFinishingTaskAsync(
        Guid operationId,
        decimal actualMinutes = 0m,
        string? rollBarcode = null,
        decimal? consumedLf = null,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var operation = await db.JobOperations
            .Include(o => o.Job).ThenInclude(j => j.Operations)
            .SingleAsync(o => o.Id == operationId, cancellationToken);

        if (operation.OperationType != JobOperationType.Finishing)
        {
            throw new InvalidOperationException("Only finishing tasks can be completed here.");
        }

        // A half-filled claim should error (inside RecordRollUsageAsync), not silently skip.
        if (!string.IsNullOrWhiteSpace(rollBarcode) || consumedLf is > 0)
        {
            await RecordRollUsageAsync(operation.JobId, rollBarcode ?? string.Empty, consumedLf ?? 0m, cancellationToken);
        }

        if (actualMinutes > 0)
        {
            operation.RecordActualMinutes(actualMinutes, userId, now);
        }

        if (operation.Status is not (JobOperationStatus.Complete or JobOperationStatus.Skipped))
        {
            operation.Complete(userId, now);
        }

        var job = operation.Job;
        var allFinishingDone = job.Operations
            .Where(o => o.OperationType == JobOperationType.Finishing)
            .All(o => o.Status is JobOperationStatus.Complete or JobOperationStatus.Skipped);

        if (allFinishingDone && job.Status == JobStatus.Printed)
        {
            job.AdvanceStatus(JobStatus.Finished, userId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs
            .Include(j => j.Operations)
            .SingleAsync(j => j.Id == jobId, cancellationToken);

        job.Close(userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<JobOperation>> BuildOperationsAsync(
        Guid jobId,
        LabelSpec spec,
        int quantityPlanned,
        Guid customerId,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken,
        bool outsourced = false)
    {
        if (outsourced)
        {
            // Made by the vendor: only the in-house receiving steps remain.
            return
            [
                JobOperation.Create(Guid.NewGuid(), jobId, 1, JobOperationType.Inspection, EquipmentType.None, null, 15, userId, now),
                JobOperation.Create(Guid.NewGuid(), jobId, 2, JobOperationType.Pack, EquipmentType.None, null, 15, userId, now),
                JobOperation.Create(Guid.NewGuid(), jobId, 3, JobOperationType.Ship, EquipmentType.None, null, 10, userId, now),
            ];
        }

        var press = await db.Presses.AsNoTracking()
            .SingleAsync(p => p.Id == Press.Indigo6800Id, cancellationToken);

        var basis = await ComputeTimeBasisAsync(spec, quantityPlanned, customerId, cancellationToken);

        var sequence = 1;
        var operations = new List<JobOperation>
        {
            JobOperation.Create(
                Guid.NewGuid(),
                jobId,
                sequence++,
                JobOperationType.Press,
                EquipmentType.Press,
                Press.Indigo6800Id,
                plannedMinutes: basis?.PressMinutes ?? press.SetupMinutes + 45,
                userId,
                now)
        };

        var finishingSelections = EstimateCalculationMapper.DeserializeFinishingOperations(spec.FinishingOperationsJson)
            .Where(s => s.OperationId != Guid.Empty)
            .ToList();
        var finishingIds = finishingSelections.Select(s => s.OperationId).ToList();
        var finishingOps = await db.FinishingOperations.AsNoTracking()
            .Where(f => finishingIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        foreach (var selection in finishingSelections.OrderBy(s => s.SortOrder))
        {
            if (!finishingOps.TryGetValue(selection.OperationId, out var finishingOp))
            {
                continue;
            }

            operations.Add(JobOperation.Create(
                Guid.NewGuid(),
                jobId,
                sequence++,
                JobOperationType.Finishing,
                EquipmentType.FinishingOperation,
                selection.OperationId,
                plannedMinutes: FinishingPlannedMinutes(selection, finishingOp, basis),
                userId,
                now));
        }

        operations.Add(JobOperation.Create(
            Guid.NewGuid(), jobId, sequence++, JobOperationType.Inspection, EquipmentType.None, null, 15, userId, now));
        operations.Add(JobOperation.Create(
            Guid.NewGuid(), jobId, sequence++, JobOperationType.Pack, EquipmentType.None, null, 15, userId, now));
        operations.Add(JobOperation.Create(
            Guid.NewGuid(), jobId, sequence, JobOperationType.Ship, EquipmentType.None, null, 10, userId, now));

        return operations;
    }

    /// <summary>Press and web-length figures computed at the job's planned quantity.</summary>
    private sealed record PlannedTimeBasis(decimal PressMinutes, decimal WebLengthFt);

    /// <summary>
    /// Runs the estimating engine against the job's spec at its planned quantity so operation
    /// plan minutes reflect the real run (imposition, waste, ink speed slowdowns), not flat
    /// allowances. Returns null when the spec can't be calculated (e.g. missing substrate),
    /// in which case callers fall back to the legacy allowances.
    /// </summary>
    private async Task<PlannedTimeBasis?> ComputeTimeBasisAsync(
        LabelSpec spec,
        int quantityPlanned,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        if (quantityPlanned <= 0)
        {
            return null;
        }

        try
        {
            var input = ToEstimateLineInput(spec, quantityPlanned);
            var request = await calculationMapper.BuildRequestAsync(customerId, input, cancellationToken);
            var result = estimatingService.Calculate(request);
            var quantityBreak = result.QuantityBreaks.FirstOrDefault();
            if (result.Errors.Count > 0 || quantityBreak is null)
            {
                return null;
            }

            // RunTimeMinutes is press setup + run at this quantity.
            return new PlannedTimeBasis(quantityBreak.RunTimeMinutes, quantityBreak.WebLengthFt);
        }
        catch
        {
            // A stale spec (deleted stock, retired ink) shouldn't block scheduling.
            return null;
        }
    }

    /// <summary>Re-expresses a job/order spec as an estimate line so the estimating engine can be
    /// run against it (planned times, and the imposition template seed).</summary>
    internal static EstimateLineFormInput ToEstimateLineInput(LabelSpec spec, int quantity) => new(
        null,
        null,
        string.Empty,
        spec.LabelAcrossIn,
        spec.LabelAroundIn,
        spec.CornerRadiusIn,
        spec.GutterAcrossIn,
        spec.GutterAroundIn,
        spec.BleedIn,
        spec.SubstrateId,
        spec.InkSet,
        spec.WhiteHits,
        spec.WhiteCoveragePct,
        EstimateCalculationMapper.DeserializeSpots(spec.SpotsJson),
        EstimateCalculationMapper.DeserializeFinishingOperations(spec.FinishingOperationsJson)
            .Where(s => s.OperationId != Guid.Empty)
            .ToList(),
        spec.SetupWasteImpressions,
        spec.RunningWastePct,
        null,
        [quantity],
        null,
        spec.MaxLabelsAcrossOverride,
        spec.LabelOrientationOverride);

    /// <summary>Setup + run time for one finishing pass, mirroring FinishingCalculator's math.</summary>
    private static decimal FinishingPlannedMinutes(
        FinishingOperationSelectionInput selection,
        FinishingOperation finishingOp,
        PlannedTimeBasis? basis)
    {
        var setupMinutes = selection.SetupMinutesOverride ?? finishingOp.DefaultSetupMinutes;
        var runSpeedFpm = selection.RunSpeedFpmOverride ?? finishingOp.DefaultRunSpeedFpm;
        return basis is not null && runSpeedFpm > 0
            ? Math.Round(setupMinutes + Math.Round(basis.WebLengthFt / runSpeedFpm, 2), 2)
            : setupMinutes + 20;
    }

    /// <summary>
    /// Recomputes plan minutes on a job's pending press/finishing operations after its spec or
    /// quantity changed (unlocked sales-order edits). Started/completed operations and the flat
    /// inspection/pack/ship allowances are left alone. Does not save — the caller's unit of work
    /// persists the changes.
    /// </summary>
    public async Task RecomputePlannedMinutesAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs
            .Include(j => j.Operations)
            .SingleAsync(j => j.Id == jobId, cancellationToken);
        if (job.Spec is null)
        {
            return;
        }

        var customerId = await db.SalesOrderLines
            .Where(l => l.Id == job.SalesOrderLineId)
            .Select(l => l.SalesOrder.CustomerId)
            .SingleAsync(cancellationToken);

        var basis = await ComputeTimeBasisAsync(job.Spec, job.QuantityPlanned, customerId, cancellationToken);
        if (basis is null)
        {
            return;
        }

        foreach (var pressOp in job.Operations.Where(o =>
                     o.OperationType == JobOperationType.Press && o.Status == JobOperationStatus.Pending))
        {
            pressOp.UpdatePlannedMinutes(basis.PressMinutes, userId, now);
        }

        var finishingSelections = EstimateCalculationMapper.DeserializeFinishingOperations(job.Spec.FinishingOperationsJson)
            .Where(s => s.OperationId != Guid.Empty)
            .OrderBy(s => s.SortOrder)
            .ToList();
        var finishingIds = finishingSelections.Select(s => s.OperationId).ToList();
        var finishingOps = await db.FinishingOperations.AsNoTracking()
            .Where(f => finishingIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        // Match spec selections to existing operations by finishing-op id, in sequence order;
        // spec edits don't add/remove job operations, so unmatched rows on either side are skipped.
        var replanned = new HashSet<Guid>();
        foreach (var selection in finishingSelections)
        {
            if (!finishingOps.TryGetValue(selection.OperationId, out var finishingOp))
            {
                continue;
            }

            var operation = job.Operations
                .Where(o => o.OperationType == JobOperationType.Finishing
                    && o.EquipmentId == selection.OperationId
                    && o.Status == JobOperationStatus.Pending
                    && !replanned.Contains(o.Id))
                .OrderBy(o => o.Sequence)
                .FirstOrDefault();
            if (operation is null)
            {
                continue;
            }

            replanned.Add(operation.Id);
            operation.UpdatePlannedMinutes(FinishingPlannedMinutes(selection, finishingOp, basis), userId, now);
        }
    }

    private static string GetOperationTypeLabel(
        JobOperation operation,
        IReadOnlyDictionary<Guid, FinishingOperation> finishing) =>
        operation.OperationType switch
        {
            JobOperationType.Press => "Press",
            JobOperationType.Finishing when operation.EquipmentId is Guid finId
                && finishing.TryGetValue(finId, out var fin) => fin.Description,
            JobOperationType.Finishing => "Finishing",
            JobOperationType.Inspection => "QC inspection",
            JobOperationType.Pack => "Pack",
            JobOperationType.Ship => "Ship",
            _ => operation.OperationType.ToString()
        };

    private async Task<JobActualCostResult> CalculateActualCostAsync(Job job, CancellationToken cancellationToken)
    {
        var presses = await db.Presses.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var finishing = await db.FinishingOperations.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var laborEntries = new List<(decimal Hours, decimal CostPerHour)>();
        foreach (var operation in job.Operations)
        {
            var hours = operation.ActualHours;
            if (hours > 0)
            {
                laborEntries.Add((hours, GetCostPerHour(operation, presses, finishing)));
            }
        }

        var materialEntries = job.MaterialUsages
            .Select(m => (m.QuantityUsedLf, m.Stock.CostPerMsi / 1000m * m.Stock.WidthIn))
            .ToList();

        // An outsourced job's real cost is what the vendor charged (GetDetailAsync's include brings the
        // OutsourcedItem along; fall back to a lookup otherwise).
        var outsideCost = 0m;
        if (job.IsOutsourced)
        {
            outsideCost = job.SalesOrderLine?.OutsourcedItem?.VendorCost
                ?? await db.OutsourcedItems.AsNoTracking()
                    .Where(o => o.SalesOrderLineId == job.SalesOrderLineId)
                    .Select(o => o.VendorCost)
                    .FirstOrDefaultAsync(cancellationToken)
                ?? 0m;
        }

        return JobCostCalculator.Calculate(laborEntries, materialEntries, outsideCost);
    }

    private async Task<decimal?> GetEstimatedCostAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.Product.SourceEstimateLineId is not Guid lineId)
        {
            return null;
        }

        var breaks = await db.EstimateQuantityBreaks.AsNoTracking()
            .Where(q => q.EstimateLineId == lineId)
            .ToListAsync(cancellationToken);

        if (breaks.Count == 0)
        {
            return null;
        }

        var breakRow = breaks
            .Where(q => q.Quantity >= job.QuantityOrdered)
            .OrderBy(q => q.Quantity)
            .FirstOrDefault()
            ?? breaks.OrderByDescending(q => q.Quantity).First();

        // An outsourced quote's cost basis is the vendor cost, not the in-house calculation.
        return breakRow.OutsourceCost ?? breakRow.CalculatedCost;
    }

    private static string? GetEquipmentName(
        JobOperation operation,
        IReadOnlyDictionary<Guid, Press> presses,
        IReadOnlyDictionary<Guid, FinishingOperation> finishing) =>
        operation.EquipmentType switch
        {
            EquipmentType.Press when operation.EquipmentId is Guid pressId && presses.TryGetValue(pressId, out var press)
                => press.Name,
            EquipmentType.FinishingOperation when operation.EquipmentId is Guid finId && finishing.TryGetValue(finId, out var fin)
                => fin.Description,
            _ => null
        };

    private static decimal GetCostPerHour(
        JobOperation operation,
        IReadOnlyDictionary<Guid, Press> presses,
        IReadOnlyDictionary<Guid, FinishingOperation> finishing) =>
        operation.EquipmentType switch
        {
            EquipmentType.Press when operation.EquipmentId is Guid pressId && presses.TryGetValue(pressId, out var press)
                => press.CostPerHour,
            EquipmentType.FinishingOperation when operation.EquipmentId is Guid finId && finishing.TryGetValue(finId, out var fin)
                => fin.CostPerHour,
            _ => 0m
        };

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
