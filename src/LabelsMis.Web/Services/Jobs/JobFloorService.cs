using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Jobs;
using LabelsMis.Domain.Search;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Jobs;

/// <summary>A job as the floor board shows it: who it's for, what it is, and where it is right now.</summary>
public record FloorJob(
    Guid Id,
    string JobNumber,
    JobStatus Status,
    bool IsOutsourced,
    Guid CustomerId,
    string CustomerName,
    string ProductDescription,
    string ProductSku,
    string? CustomerSku,
    Guid SalesOrderId,
    string OrderNumber,
    string? CustomerPoNumber,
    DateOnly? DueDate,
    DateOnly? ScheduledForDate,
    int QuantityOrdered,
    string? Notes,
    bool ShipmentRecordRequired,
    string? ShippingMethodName,
    DateTime CreatedAt,
    JobFloorLocation Location)
{
    public bool IsLive => JobService.LiveStatuses.Contains(Status);
}

/// <summary>Filters for the jobs floor. Null/empty means "don't filter on it".</summary>
public sealed record FloorQuery(
    string? Search = null,
    IReadOnlyCollection<JobStatus>? Statuses = null,
    Guid? CustomerId = null,
    Guid? PressId = null,
    DateOnly? DueFrom = null,
    DateOnly? DueTo = null,
    DateOnly? ScheduledDate = null,
    string? Sort = null);

/// <summary>
/// Reads jobs for the floor board (Jobs page) and for job hits in the global search: the list
/// fields plus each job's route, resolved into a <see cref="JobFloorLocation"/> — stage, station,
/// step within the stage, and who is clocked on.
/// </summary>
public class JobFloorService(LabelsMisDbContext db, ClientClock clock)
{
    /// <summary>Upper bound on one live-floor load; a single-press shop has a few dozen live jobs.</summary>
    public const int MaxJobs = 1000;

    public async Task<PagedResult<FloorJob>> ListAsync(
        FloorQuery filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, MaxJobs);

        var query = Sort(Filter(filter), filter.Sort);
        var total = await query.CountAsync(cancellationToken);
        var rows = await Project(query.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        var jobs = await LocateAsync(rows, cancellationToken);
        return new PagedResult<FloorJob>(jobs, page, pageSize, total);
    }

    /// <summary>Jobs per status under the same filters, ignoring the status and text filters — the
    /// stage tabs show what each stage holds whichever one is selected.</summary>
    public async Task<IReadOnlyDictionary<JobStatus, int>> CountByStatusAsync(
        FloorQuery filter,
        CancellationToken cancellationToken = default)
    {
        var counts = await Filter(filter with { Statuses = null, Search = null })
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(c => c.Status, c => c.Count);
    }

    /// <summary>The given jobs with their floor locations, in no particular order.</summary>
    public async Task<IReadOnlyList<FloorJob>> GetAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
        {
            return [];
        }

        var rows = await Project(db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id))).ToListAsync(cancellationToken);
        return await LocateAsync(rows, cancellationToken);
    }

    private IQueryable<Domain.Entities.Job> Filter(FloorQuery filter)
    {
        var query = db.Jobs.AsNoTracking();

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            query = query.Where(j => statuses.Contains(j.Status));
        }

        if (filter.CustomerId is { } customerId)
        {
            query = query.Where(j => j.SalesOrderLine.SalesOrder.CustomerId == customerId);
        }

        if (filter.PressId is { } pressId)
        {
            query = query.Where(j => j.ScheduledPressId == pressId);
        }

        if (filter.DueFrom is { } dueFrom)
        {
            query = query.Where(j => j.DueDate >= dueFrom);
        }

        if (filter.DueTo is { } dueTo)
        {
            query = query.Where(j => j.DueDate <= dueTo);
        }

        if (filter.ScheduledDate is { } scheduled)
        {
            query = query.Where(j => j.ScheduledForDate == scheduled);
        }

        // Every term has to match somewhere: "acme cherry" finds Acme's cherry label.
        foreach (var term in SearchQuery.Parse(filter.Search).Terms)
        {
            query = query.Where(j =>
                j.JobNumber.ToUpper().Contains(term)
                || j.Product.Description.ToUpper().Contains(term)
                || j.Product.InternalSku.ToUpper().Contains(term)
                || (j.Product.CustomerSku != null && j.Product.CustomerSku.ToUpper().Contains(term))
                || (j.SalesOrderLine.Description != null && j.SalesOrderLine.Description.ToUpper().Contains(term))
                || j.SalesOrderLine.SalesOrder.OrderNumber.ToUpper().Contains(term)
                || (j.SalesOrderLine.SalesOrder.CustomerPoNumber != null && j.SalesOrderLine.SalesOrder.CustomerPoNumber.ToUpper().Contains(term))
                || j.SalesOrderLine.SalesOrder.Customer.Name.ToUpper().Contains(term));
        }

        return query;
    }

    private static IQueryable<Domain.Entities.Job> Sort(IQueryable<Domain.Entities.Job> query, string? sort)
    {
        var (key, desc) = QueryExtensions.ParseSort(sort);
        return key switch
        {
            "number" => query.OrderByDir(desc, j => j.JobNumber),
            "order" => query.OrderByDir(desc, j => j.SalesOrderLine.SalesOrder.OrderNumber).ThenBy(j => j.JobNumber),
            "customer" => query.OrderByDir(desc, j => j.SalesOrderLine.SalesOrder.Customer.Name).ThenBy(j => j.DueDate),
            "product" => query.OrderByDir(desc, j => j.SalesOrderLine.Description ?? j.Product.Description),
            "status" or "where" => query.OrderByDir(desc, j => j.Status).ThenBy(j => j.DueDate).ThenBy(j => j.JobNumber),
            "scheduled" => query.OrderByDir(desc, j => j.ScheduledForDate).ThenBy(j => j.JobNumber),
            "qty" => query.OrderByDir(desc, j => j.QuantityOrdered),
            "due" => query.OrderByDir(desc, j => j.DueDate).ThenBy(j => j.JobNumber),
            _ => query.OrderBy(j => j.DueDate).ThenBy(j => j.JobNumber)
        };
    }

    private sealed record FloorRow(
        Guid Id,
        string JobNumber,
        JobStatus Status,
        bool IsOutsourced,
        Guid CustomerId,
        string CustomerName,
        string ProductDescription,
        string ProductSku,
        string? CustomerSku,
        Guid SalesOrderId,
        string OrderNumber,
        string? CustomerPoNumber,
        DateOnly? DueDate,
        DateOnly? ScheduledForDate,
        Guid? ScheduledPressId,
        int QuantityOrdered,
        string? Notes,
        bool ShipmentRecordRequired,
        string? ShippingMethodName,
        bool IsImposed,
        bool ImposedIsStale,
        string? VendorName,
        DateOnly? VendorExpectedIn,
        DateTime CreatedAt);

    private static IQueryable<FloorRow> Project(IQueryable<Domain.Entities.Job> query) =>
        query.Select(j => new FloorRow(
            j.Id,
            j.JobNumber,
            j.Status,
            j.IsOutsourced,
            j.SalesOrderLine.SalesOrder.CustomerId,
            j.SalesOrderLine.SalesOrder.Customer.Name,
            j.SalesOrderLine.Description ?? j.Product.Description,
            j.Product.InternalSku,
            j.Product.CustomerSku,
            j.SalesOrderLine.SalesOrderId,
            j.SalesOrderLine.SalesOrder.OrderNumber,
            j.SalesOrderLine.SalesOrder.CustomerPoNumber,
            j.DueDate,
            j.ScheduledForDate,
            j.ScheduledPressId,
            j.QuantityOrdered,
            j.Notes,
            j.SalesOrderLine.SalesOrder.ShippingMethod == null || j.SalesOrderLine.SalesOrder.ShippingMethod.RequiresShipmentRecord,
            j.SalesOrderLine.SalesOrder.ShippingMethod != null ? j.SalesOrderLine.SalesOrder.ShippingMethod.Name : null,
            j.ImposedArtworkFilePath != null,
            j.ImposedArtworkFilePath != null && !j.ImposedIsManual && j.ImposedFromArtworkFilePath != j.Product.ArtworkFilePath,
            j.SalesOrderLine.OutsourcedItem != null && j.SalesOrderLine.OutsourcedItem.Vendor != null
                ? j.SalesOrderLine.OutsourcedItem.Vendor.Name
                : null,
            j.SalesOrderLine.OutsourcedItem != null ? j.SalesOrderLine.OutsourcedItem.ExpectedIn : null,
            j.CreatedAt));

    private async Task<List<FloorJob>> LocateAsync(IReadOnlyList<FloorRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var operations = await db.JobOperations.AsNoTracking()
            .Where(o => ids.Contains(o.JobId))
            .Select(o => new
            {
                o.JobId,
                o.Sequence,
                o.OperationType,
                o.EquipmentType,
                o.EquipmentId,
                o.Status,
                ClockedOnUserId = o.TimeEntries
                    .Where(t => t.ClockedOutAt == null)
                    .Select(t => (Guid?)t.UserId)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var presses = await db.Presses.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var finishing = await db.FinishingOperations.AsNoTracking().ToDictionaryAsync(f => f.Id, cancellationToken);

        var userIds = operations.Where(o => o.ClockedOnUserId is not null).Select(o => o.ClockedOnUserId!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => ShortName(u.Email ?? u.UserName), cancellationToken);

        var byJob = operations.ToLookup(o => o.JobId);
        var today = clock.Today;

        return rows.Select(row =>
        {
            var jobOperations = byJob[row.Id].ToList();
            var pressOperationId = jobOperations
                .FirstOrDefault(o => o.OperationType == JobOperationType.Press && o.EquipmentType == EquipmentType.Press)
                ?.EquipmentId;
            var pressName = row.ScheduledPressId is { } scheduledPress && presses.TryGetValue(scheduledPress, out var scheduledName)
                ? scheduledName
                : pressOperationId is { } pressId && presses.TryGetValue(pressId, out var routeName) ? routeName : null;

            var facts = new JobFloorFacts(
                row.Status,
                row.IsOutsourced,
                jobOperations
                    .Select(o => new FloorOperation(
                        o.Sequence,
                        o.OperationType,
                        o.Status,
                        JobService.OperationLabel(o.OperationType, o.EquipmentId, finishing),
                        o.ClockedOnUserId is { } uid ? users.GetValueOrDefault(uid, "someone") : null))
                    .ToList(),
                row.IsImposed,
                row.ImposedIsStale,
                pressName,
                row.VendorName,
                row.VendorExpectedIn,
                row.ShippingMethodName,
                row.ShipmentRecordRequired);

            return new FloorJob(
                row.Id,
                row.JobNumber,
                row.Status,
                row.IsOutsourced,
                row.CustomerId,
                row.CustomerName,
                row.ProductDescription,
                row.ProductSku,
                row.CustomerSku,
                row.SalesOrderId,
                row.OrderNumber,
                row.CustomerPoNumber,
                row.DueDate,
                row.ScheduledForDate,
                row.QuantityOrdered,
                row.Notes,
                row.ShipmentRecordRequired,
                row.ShippingMethodName,
                row.CreatedAt,
                JobFloorLocator.Locate(facts, today));
        }).ToList();
    }

    /// <summary>"tom@heyluckylabel.com" → "tom": enough to say who is on a job.</summary>
    private static string ShortName(string? login) =>
        string.IsNullOrWhiteSpace(login) ? "someone" : login.Split('@')[0];
}
