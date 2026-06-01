using System.Text.Json;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Jobs;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Models;
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
    int QuantityOrdered);

public record JobCostSummary(
    decimal? EstimatedCost,
    JobActualCostResult ActualCost);

public record JobDetail(
    Job Job,
    string CustomerName,
    string ProductDescription,
    string? ArtworkFilePath,
    Stock Substrate,
    JobCostSummary CostSummary,
    IReadOnlyList<(JobOperation Operation, string? EquipmentName, string TypeLabel)> Operations,
    Guid SalesOrderId,
    string OrderNumber,
    string? OrderNotes);

public record JobTicketDetail(
    Job Job,
    string CustomerName,
    string ProductDescription,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    string SubstrateDescription,
    InkSet InkSet,
    IReadOnlyList<(int Sequence, JobOperationType Type, string Description)> Route);

public record OperatorJobView(
    Job Job,
    string CustomerName,
    string ProductDescription,
    JobOperation? CurrentOperation,
    bool IsClockedOn,
    Guid? ScannedRollId,
    string? ScannedRollBarcode);

public record ScheduleJobInput(DateOnly ScheduledForDate, Guid? PressId);

public record FinishingTaskView(Guid OperationId, string Label, JobOperationStatus Status)
{
    public bool IsDone => Status is JobOperationStatus.Complete or JobOperationStatus.Skipped;
}

public record FinishingJobView(
    Guid JobId,
    string JobNumber,
    string CustomerName,
    string ProductDescription,
    DateOnly? DueDate,
    int QuantityOrdered,
    IReadOnlyList<FinishingTaskView> Tasks);

public class JobService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers)
{
    /// <summary>Job statuses considered "live" — still moving through production.</summary>
    public static readonly IReadOnlyList<JobStatus> LiveStatuses =
    [
        JobStatus.PrePress, JobStatus.Queued, JobStatus.Printed, JobStatus.Finished, JobStatus.Rewound
    ];

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
                || j.Product.PrimaryCustomer.Name.ToUpper().Contains(term));
        }

        query = sort switch
        {
            "due" => query.OrderBy(j => j.DueDate).ThenBy(j => j.Priority),
            "priority" => query.OrderBy(j => j.Priority).ThenBy(j => j.DueDate),
            "status" => query.OrderBy(j => j.Status).ThenByDescending(j => j.CreatedAt),
            "number" => query.OrderBy(j => j.JobNumber),
            _ => query.OrderBy(j => j.Priority).ThenBy(j => j.DueDate)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobListItem(
                j.Id,
                j.JobNumber,
                j.Product.PrimaryCustomer.Name,
                j.Product.Description,
                j.Status,
                j.DueDate,
                j.ScheduledForDate,
                j.Priority,
                j.QuantityOrdered))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobListItem>(items, page, pageSize, total);
    }

    public async Task<JobDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.Product).ThenInclude(p => p.Substrate)
            .Include(j => j.Operations).ThenInclude(o => o.TimeEntries)
            .Include(j => j.MaterialUsages).ThenInclude(m => m.Stock)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder)
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
            job.Product.PrimaryCustomer.Name,
            job.Product.Description,
            job.Product.ArtworkFilePath,
            job.Product.Substrate,
            new JobCostSummary(estimatedCost, actualCost),
            operations,
            order.Id,
            order.OrderNumber,
            order.Notes);
    }

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
            .Include(j => j.Operations)
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return null;
        }

        var finishing = await db.FinishingOperations.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, cancellationToken);

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
            return (o.Sequence, o.OperationType, description);
        }).ToList();

        return new JobTicketDetail(
            job,
            job.Product.PrimaryCustomer.Name,
            job.Product.Description,
            job.Product.LabelAcrossIn,
            job.Product.LabelAroundIn,
            job.Product.Substrate.Description,
            job.Product.InkSet,
            route);
    }

    public async Task<OperatorJobView?> GetOperatorViewAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
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
            job.Product.PrimaryCustomer.Name,
            job.Product.Description,
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
                userId,
                now);

            var operations = await BuildOperationsAsync(
                job.Id, line.Product, line.SourceEstimateLineId, userId, now, cancellationToken);
            foreach (var operation in operations)
            {
                job.AddOperation(operation);
                db.JobOperations.Add(operation);
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

    public async Task SetJobStatusAsync(
        Guid jobId,
        JobStatus status,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        job.SetStatus(status, userId, now);
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

        return wanted.ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0);
    }

    /// <summary>Advances a job forward to the given status (used by the production stage pages).</summary>
    public async Task AdvanceJobStatusAsync(
        Guid jobId,
        JobStatus target,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var job = await db.Jobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        job.AdvanceStatus(target, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Jobs currently in the Printed (finishing) stage, with their finishing tasks.</summary>
    public async Task<IReadOnlyList<FinishingJobView>> ListFinishingJobsAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.Jobs.AsNoTracking()
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.Operations)
            .Where(j => j.Status == JobStatus.Printed);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(j =>
                j.JobNumber.ToUpper().Contains(term)
                || j.Product.Description.ToUpper().Contains(term)
                || j.Product.PrimaryCustomer.Name.ToUpper().Contains(term));
        }

        var jobs = await query.OrderBy(j => j.DueDate).ThenBy(j => j.Priority).ToListAsync(cancellationToken);
        var finishing = await db.FinishingOperations.AsNoTracking().ToDictionaryAsync(f => f.Id, cancellationToken);

        return jobs.Select(j => new FinishingJobView(
            j.Id,
            j.JobNumber,
            j.Product.PrimaryCustomer.Name,
            j.Product.Description,
            j.DueDate,
            j.QuantityOrdered,
            j.Operations
                .Where(o => o.OperationType == JobOperationType.Finishing)
                .OrderBy(o => o.Sequence)
                .Select(o => new FinishingTaskView(o.Id, GetOperationTypeLabel(o, finishing), o.Status))
                .ToList()))
            .ToList();
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
            throw new InvalidOperationException("Scan or enter a roll barcode.");
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
    /// Marks a finishing task complete. When the last finishing task on a Printed job is done,
    /// the job auto-advances to Finished so it moves on to the Rewinding stage.
    /// </summary>
    public async Task CompleteFinishingTaskAsync(Guid operationId, CancellationToken cancellationToken = default)
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
        Product product,
        Guid? sourceEstimateLineId,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var press = await db.Presses.AsNoTracking()
            .SingleAsync(p => p.Id == Press.Indigo6800Id, cancellationToken);

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
                plannedMinutes: press.SetupMinutes + 45,
                userId,
                now)
        };

        var finishingJson = await ResolveFinishingOperationsJsonAsync(product, sourceEstimateLineId, cancellationToken);
        var finishingSelections = EstimateCalculationMapper.DeserializeFinishingOperations(finishingJson)
            .Where(s => s.OperationId != Guid.Empty)
            .ToList();
        var finishingIds = finishingSelections.Select(s => s.OperationId).ToList();
        var finishingOps = await db.FinishingOperations.AsNoTracking()
            .Where(f => finishingIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        foreach (var selection in finishingSelections.OrderBy(s => s.SortOrder))
        {
            if (!finishingOps.ContainsKey(selection.OperationId))
            {
                continue;
            }

            var setupMinutes = selection.SetupMinutesOverride
                ?? finishingOps[selection.OperationId].DefaultSetupMinutes;
            operations.Add(JobOperation.Create(
                Guid.NewGuid(),
                jobId,
                sequence++,
                JobOperationType.Finishing,
                EquipmentType.FinishingOperation,
                selection.OperationId,
                plannedMinutes: setupMinutes + 20,
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

    private async Task<string> ResolveFinishingOperationsJsonAsync(
        Product product,
        Guid? sourceEstimateLineId,
        CancellationToken cancellationToken)
    {
        if (!IsEmptyJsonArray(product.FinishingOperationsJson))
        {
            return product.FinishingOperationsJson;
        }

        var lineIds = new[] { sourceEstimateLineId, product.SourceEstimateLineId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        foreach (var lineId in lineIds)
        {
            var lineJson = await db.EstimateLines.AsNoTracking()
                .Where(l => l.Id == lineId)
                .Select(l => l.FinishingOperationsJson)
                .SingleOrDefaultAsync(cancellationToken);

            if (!IsEmptyJsonArray(lineJson))
            {
                return lineJson!;
            }
        }

        return "[]";
    }

    private static bool IsEmptyJsonArray(string? json) =>
        string.IsNullOrWhiteSpace(json) || json.Trim() is "[]" or "{}" or "null";

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
            var costPerHour = GetCostPerHour(operation, presses, finishing);
            foreach (var entry in operation.TimeEntries.Where(t => t.ClockedOutAt.HasValue))
            {
                laborEntries.Add((entry.DurationHours, costPerHour));
            }
        }

        var materialEntries = job.MaterialUsages
            .Select(m => (m.QuantityUsedLf, m.Stock.CostPerMsi / 1000m * m.Stock.WidthIn))
            .ToList();

        return JobCostCalculator.Calculate(laborEntries, materialEntries);
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

        return breakRow.CalculatedCost;
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
