using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

using LabelsMis.Web.Services.Products;

namespace LabelsMis.Web.Services.SalesOrders;

public record SalesOrderLineInput(
    Guid? Id,
    Guid ProductId,
    Guid? SourceEstimateLineId,
    int Quantity,
    decimal UnitPrice,
    string? LineNotes,
    string? Description = null,
    LabelSpec? Spec = null);

public record SalesOrderFormInput(
    Guid CustomerId,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    string? Notes,
    IReadOnlyList<SalesOrderLineInput> Lines,
    Guid? ShippingMethodId,
    decimal ShippingCost,
    ShippingAddress ShippingAddress);

public record EstimateConversionLineInput(
    Guid EstimateLineId,
    int Quantity,
    decimal UnitPrice,
    string? LineNotes);

public record EstimateConversionInput(
    Guid EstimateId,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    string? Notes,
    IReadOnlyList<EstimateConversionLineInput> Lines);

public record SalesOrderListItem(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    SalesOrderStatus Status,
    decimal OrderTotal,
    DateTime OrderedAt);

public class SalesOrderService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    ProductService productService,
    Invoices.InvoiceService invoiceService,
    Jobs.JobService jobService)
{
    public async Task<PagedResult<SalesOrderListItem>> ListAsync(
        string? search,
        SalesOrderStatus? status,
        Guid? customerId,
        DateOnly? shipFrom,
        DateOnly? shipTo,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.SalesOrders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(o => o.CustomerId == customerId.Value);
        }

        if (shipFrom.HasValue)
        {
            query = query.Where(o => o.RequestedShipDate >= shipFrom.Value);
        }

        if (shipTo.HasValue)
        {
            query = query.Where(o => o.RequestedShipDate <= shipTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(o =>
                o.OrderNumber.ToUpper().Contains(term)
                || (o.CustomerPoNumber != null && o.CustomerPoNumber.ToUpper().Contains(term))
                || o.Customer.Name.ToUpper().Contains(term));
        }

        query = sort switch
        {
            "number" => query.OrderBy(o => o.OrderNumber),
            "ship" => query.OrderBy(o => o.RequestedShipDate),
            "status" => query.OrderBy(o => o.Status),
            _ => query.OrderByDescending(o => o.OrderedAt)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new SalesOrderListItem(
                o.Id,
                o.OrderNumber,
                o.Customer.Name,
                o.CustomerPoNumber,
                o.RequestedShipDate,
                o.Status,
                o.Lines.Sum(l => l.LineTotal),
                o.OrderedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<SalesOrderListItem>(items, page, pageSize, total);
    }

    public async Task<SalesOrder?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.ShippingMethod)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<SalesOrder> CreateAsync(SalesOrderFormInput input, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        ValidateLines(input.Lines);

        if (input.CustomerId == Guid.Empty
            || !await db.Customers.AnyAsync(c => c.Id == input.CustomerId, cancellationToken))
        {
            throw new InvalidOperationException("Select a customer before saving the order.");
        }

        await ValidateShippingMethodAsync(input.ShippingMethodId, cancellationToken);
        var orderNumber = await documentNumbers.NextSalesOrderNumberAsync(cancellationToken);
        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(),
            orderNumber,
            input.CustomerId,
            sourceEstimateId: null,
            salesRepId: null,
            input.CustomerPoNumber,
            now,
            input.RequestedShipDate,
            input.Notes,
            input.ShippingMethodId,
            input.ShippingCost,
            input.ShippingAddress,
            userId,
            now);

        var seedSpecs = await LoadProductSeedSpecsAsync(input.Lines, cancellationToken);
        var lines = BuildLines(order.Id, input.Lines, seedSpecs, userId, now);
        order.ReplaceLines(lines);
        db.SalesOrders.Add(order);
        foreach (var line in lines)
        {
            db.SalesOrderLines.Add(line);
        }

        await db.SaveChangesAsync(cancellationToken);
        await invoiceService.CreateFromSalesOrderAsync(order.Id, cancellationToken);
        return order;
    }

    public async Task<SalesOrder> CreateFromEstimateAsync(
        EstimateConversionInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        if (input.Lines.Count == 0)
        {
            throw new InvalidOperationException("At least one line is required to convert an estimate.");
        }

        var estimate = await db.Estimates
            .Include(e => e.Lines)
            .SingleAsync(e => e.Id == input.EstimateId, cancellationToken);

        if (estimate.Status is not Domain.Enums.EstimateStatus.Won)
        {
            throw new InvalidOperationException("Only won estimates can be converted to a sales order.");
        }

        var existing = await db.SalesOrders.AnyAsync(o => o.SourceEstimateId == input.EstimateId, cancellationToken);
        if (existing)
        {
            throw new InvalidOperationException("A sales order already exists for this estimate.");
        }

        var orderNumber = await documentNumbers.NextSalesOrderNumberAsync(cancellationToken);
        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(),
            orderNumber,
            estimate.CustomerId,
            estimate.Id,
            estimate.SalesRepId,
            input.CustomerPoNumber,
            now,
            input.RequestedShipDate,
            input.Notes,
            estimate.ShippingMethodId,
            estimate.ShippingCost,
            estimate.ShippingAddress,
            userId,
            now);

        db.SalesOrders.Add(order);

        var estimateLines = estimate.Lines.ToDictionary(l => l.Id);
        var orderLines = new List<SalesOrderLine>();

        for (var i = 0; i < input.Lines.Count; i++)
        {
            var lineInput = input.Lines[i];
            if (!estimateLines.TryGetValue(lineInput.EstimateLineId, out var estimateLine))
            {
                throw new InvalidOperationException("Estimate line not found on estimate.");
            }

            var product = await productService.EnsureProductForLineAsync(estimateLine.Id, userId, now, cancellationToken);

            // Snapshot the spec the customer was quoted, pinning the product's die + artwork.
            var spec = estimateLine.ToLabelSpec(product.DieId, product.ArtworkFilePath);

            var orderLine = SalesOrderLine.Create(
                Guid.NewGuid(),
                order.Id,
                i + 1,
                product.Id,
                estimateLine.ProductDescription,
                estimateLine.Id,
                lineInput.Quantity,
                lineInput.UnitPrice,
                lineInput.LineNotes,
                spec,
                userId,
                now);
            orderLines.Add(orderLine);
            db.SalesOrderLines.Add(orderLine);
        }

        order.ReplaceLines(orderLines);
        await db.SaveChangesAsync(cancellationToken);
        await invoiceService.CreateFromSalesOrderAsync(order.Id, cancellationToken);
        return order;
    }

    public async Task UpdateAsync(
        Guid id,
        SalesOrderFormInput input,
        bool adminOverride,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        ValidateLines(input.Lines);

        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .SingleAsync(o => o.Id == id, cancellationToken);

        if (order.Status is not SalesOrderStatus.Open && !adminOverride)
        {
            order.EnsureOpen();
        }

        await ValidateShippingMethodAsync(input.ShippingMethodId, cancellationToken);
        order.UpdateOpen(
            input.CustomerPoNumber,
            input.RequestedShipDate,
            input.Notes,
            input.ShippingMethodId,
            input.ShippingCost,
            input.ShippingAddress,
            userId,
            now);
        db.SalesOrderLines.RemoveRange(order.Lines);
        var seedSpecs = await LoadProductSeedSpecsAsync(input.Lines, cancellationToken);
        var lines = BuildLines(order.Id, input.Lines, seedSpecs, userId, now);
        order.ReplaceLines(lines);
        foreach (var line in lines)
        {
            db.SalesOrderLines.Add(line);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The explicit "unlocked" edit path for orders already in production. Lines are updated
    /// in place (matched by id) so job references stay intact, and line changes are propagated
    /// to the linked jobs: spec edits via <see cref="Job.SetSpec"/>, quantity changes via
    /// <see cref="Job.UpdateOrderedQuantity"/>. Lines can be added; a line can only be removed
    /// while no job references it. Products on job-linked lines cannot change.
    /// </summary>
    public async Task UpdateInProductionAsync(
        Guid id,
        SalesOrderFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        ValidateLines(input.Lines);

        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .SingleAsync(o => o.Id == id, cancellationToken);

        if (order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed)
        {
            throw new InvalidOperationException("Cancelled or closed orders cannot be edited.");
        }

        await ValidateShippingMethodAsync(input.ShippingMethodId, cancellationToken);
        order.UpdateDetails(
            input.CustomerPoNumber,
            input.RequestedShipDate,
            input.Notes,
            input.ShippingMethodId,
            input.ShippingCost,
            input.ShippingAddress,
            userId,
            now);

        var existingLines = order.Lines.ToDictionary(l => l.Id);
        var lineIds = order.Lines.Select(l => l.Id).ToList();
        var jobsByLine = (await db.Jobs
                .Where(j => lineIds.Contains(j.SalesOrderLineId) && j.Status != JobStatus.Closed)
                .ToListAsync(cancellationToken))
            .ToLookup(j => j.SalesOrderLineId);

        var keptIds = new HashSet<Guid>();
        var replanJobIds = new List<Guid>();
        var seedSpecs = await LoadProductSeedSpecsAsync(input.Lines, cancellationToken);

        foreach (var lineInput in input.Lines)
        {
            if (lineInput.Id is { } lineId && existingLines.TryGetValue(lineId, out var line))
            {
                keptIds.Add(lineId);
                var lineJobs = jobsByLine[lineId].ToList();
                if (lineJobs.Count > 0 && lineInput.ProductId != line.ProductId)
                {
                    throw new InvalidOperationException(
                        $"Line {line.LineNumber} already has a job — its product cannot change. Remove the job first or add a new line.");
                }

                var quantityChanged = line.Quantity != lineInput.Quantity;
                line.Update(lineInput.Quantity, lineInput.UnitPrice, lineInput.LineNotes, userId, now);
                line.UpdateDescription(lineInput.Description, userId, now);
                if (lineInput.Spec is { } spec)
                {
                    line.SetSpec(spec, userId, now);
                }

                foreach (var job in lineJobs)
                {
                    if (lineInput.Spec is { } jobSpec)
                    {
                        job.SetSpec(jobSpec, userId, now);
                    }

                    if (quantityChanged)
                    {
                        job.UpdateOrderedQuantity(lineInput.Quantity, userId, now);
                    }

                    if (lineInput.Spec is not null || quantityChanged)
                    {
                        replanJobIds.Add(job.Id);
                    }
                }
            }
            else
            {
                var newLine = SalesOrderLine.Create(
                    Guid.NewGuid(),
                    order.Id,
                    order.Lines.Count + 1,
                    lineInput.ProductId,
                    lineInput.Description,
                    lineInput.SourceEstimateLineId,
                    lineInput.Quantity,
                    lineInput.UnitPrice,
                    lineInput.LineNotes,
                    lineInput.Spec ?? seedSpecs.GetValueOrDefault(lineInput.ProductId),
                    userId,
                    now);
                order.AddLine(newLine);
                db.SalesOrderLines.Add(newLine);
                keptIds.Add(newLine.Id);
            }
        }

        foreach (var removed in existingLines.Values.Where(l => !keptIds.Contains(l.Id)).ToList())
        {
            if (jobsByLine[removed.Id].Any())
            {
                throw new InvalidOperationException(
                    $"Line {removed.LineNumber} has a job in production and cannot be removed.");
            }

            db.SalesOrderLines.Remove(removed);
        }

        // Spec/quantity changes invalidate the plan minutes on pending operations.
        foreach (var jobId in replanJobIds)
        {
            await jobService.RecomputePlannedMinutesAsync(jobId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Sales order not found.");

        order.EnsureCanDelete();

        var lineIds = order.Lines.Select(l => l.Id).ToList();
        if (await db.Jobs.AnyAsync(j => lineIds.Contains(j.SalesOrderLineId), cancellationToken))
        {
            throw new InvalidOperationException("Cannot delete an order that has scheduled jobs.");
        }
        if (await db.Invoices.AnyAsync(i => i.SalesOrderId == id, cancellationToken))
        {
            throw new InvalidOperationException("Cannot delete an order that has invoices.");
        }
        if (await db.Shipments.AnyAsync(s => s.SalesOrderId == id, cancellationToken))
        {
            throw new InvalidOperationException("Cannot delete an order that has shipments.");
        }

        db.SalesOrderLines.RemoveRange(order.Lines);
        db.SalesOrders.Remove(order);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Cancels an order that can't be deleted (it's in production or has downstream records) and
    /// deactivates everything tied to it: jobs are closed and non-paid invoices are voided.
    /// Shipments have no cancel state and are left as historical records.
    /// </summary>
    public async Task CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Sales order not found.");

        order.Cancel(userId, now);

        var lineIds = order.Lines.Select(l => l.Id).ToList();
        var jobs = await db.Jobs
            .Where(j => lineIds.Contains(j.SalesOrderLineId) && j.Status != JobStatus.Closed)
            .ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.SetStatus(JobStatus.Closed, userId, now);
        }

        var invoices = await db.Invoices
            .Where(i => i.SalesOrderId == id
                && i.Status != InvoiceStatus.Void
                && i.Status != InvoiceStatus.Paid)
            .ToListAsync(cancellationToken);
        foreach (var invoice in invoices)
        {
            invoice.Void("Sales order cancelled.", userId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<decimal?> GetSuggestedUnitPriceAsync(
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default) =>
        await productService.GetDefaultUnitPriceAsync(productId, quantity, cancellationToken);

    private static void ValidateLines(IReadOnlyList<SalesOrderLineInput> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("At least one line item is required.");
        }
    }

    private static List<SalesOrderLine> BuildLines(
        Guid salesOrderId,
        IReadOnlyList<SalesOrderLineInput> lines,
        IReadOnlyDictionary<Guid, LabelSpec> productSeedSpecs,
        Guid userId,
        DateTime now)
    {
        return lines
            .Select((line, index) =>
                SalesOrderLine.Create(
                    line.Id ?? Guid.NewGuid(),
                    salesOrderId,
                    index + 1,
                    line.ProductId,
                    line.Description,
                    line.SourceEstimateLineId,
                    line.Quantity,
                    line.UnitPrice,
                    line.LineNotes,
                    // Preserve the spec carried through the form; otherwise seed from the product.
                    line.Spec ?? productSeedSpecs.GetValueOrDefault(line.ProductId),
                    userId,
                    now))
            .ToList();
    }

    private async Task<Dictionary<Guid, LabelSpec>> LoadProductSeedSpecsAsync(
        IReadOnlyList<SalesOrderLineInput> lines,
        CancellationToken cancellationToken)
    {
        var productIds = lines.Select(l => l.ProductId).Where(id => id != Guid.Empty).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        return products.ToDictionary(p => p.Id, p => p.ToLabelSpec());
    }

    private async Task ValidateShippingMethodAsync(Guid? shippingMethodId, CancellationToken cancellationToken)
    {
        if (shippingMethodId is not { } id || id == Guid.Empty)
        {
            return;
        }

        var exists = await db.ShippingMethods.AnyAsync(m => m.Id == id, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Selected shipping method was not found.");
        }
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
