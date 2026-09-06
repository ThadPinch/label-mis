using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.Outsourcing;
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
    LabelSpec? Spec = null,
    OutsourceItemInput? Outsource = null);

/// <summary>A flat, non-label item on the order: a one-time charge (die creation, design time) or an
/// outsourced item (promo, print, wide format). Invoiced with the order and shown on the job ticket;
/// never scheduled as a production job — outsourced ones are tracked on the production Outsourced page.</summary>
public record SalesOrderChargeInput(
    Guid? Id,
    string Description,
    int Quantity,
    decimal UnitPrice,
    Guid? SourceEstimateChargeId = null,
    OutsourceItemInput? Outsource = null);

public record SalesOrderFormInput(
    Guid CustomerId,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    string? Notes,
    IReadOnlyList<SalesOrderLineInput> Lines,
    Guid? ShippingMethodId,
    decimal ShippingCost,
    ShippingAddress ShippingAddress,
    string? BillingNotes = null,
    IReadOnlyList<SalesOrderChargeInput>? Charges = null);

public record EstimateConversionLineInput(
    Guid EstimateLineId,
    int Quantity,
    decimal UnitPrice,
    string? LineNotes);

public record EstimateConversionChargeInput(
    Guid? EstimateChargeId,
    string Description,
    int Quantity,
    decimal UnitPrice);

public record EstimateConversionInput(
    Guid EstimateId,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    string? Notes,
    IReadOnlyList<EstimateConversionLineInput> Lines,
    string? BillingNotes = null,
    IReadOnlyList<EstimateConversionChargeInput>? Charges = null);

public record SalesOrderListItem(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    SalesOrderStatus Status,
    decimal OrderTotal,
    DateTime OrderedAt);

public record SalesOrderPackingListPdf(string OrderNumber, byte[] Bytes);

public class SalesOrderService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    ProductService productService,
    Invoices.InvoiceService invoiceService,
    Jobs.JobService jobService,
    Pdf.PackingListPdfGenerator packingListPdfGenerator)
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

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "number" => query.OrderByDir(desc, o => o.OrderNumber),
            "customer" => query.OrderByDir(desc, o => o.Customer.Name),
            "po" => query.OrderByDir(desc, o => o.CustomerPoNumber),
            "ship" => query.OrderByDir(desc, o => o.RequestedShipDate),
            "status" => query.OrderByDir(desc, o => o.Status),
            "total" => query.OrderByDir(desc, o => o.Lines.Sum(l => l.LineTotal) + o.Charges.Sum(c => c.LineTotal)),
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
                o.Lines.Sum(l => l.LineTotal) + o.Charges.Sum(c => c.LineTotal),
                o.OrderedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<SalesOrderListItem>(items, page, pageSize, total);
    }

    public async Task<SalesOrder?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.ShippingMethod)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Lines).ThenInclude(l => l.OutsourcedItem).ThenInclude(i => i!.Vendor)
            .Include(o => o.Lines).ThenInclude(l => l.OutsourcedItem).ThenInclude(i => i!.Receipts)
            .Include(o => o.Charges).ThenInclude(c => c.OutsourcedItem).ThenInclude(i => i!.Vendor)
            .Include(o => o.Charges).ThenInclude(c => c.OutsourcedItem).ThenInclude(i => i!.Receipts)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    /// <summary>Renders the packing-list PDF for an order; null when the order doesn't exist.
    /// The date and box count come from recorded shipments when any exist.</summary>
    public async Task<SalesOrderPackingListPdf?> RenderPackingListPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetAsync(id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrderId == id)
            .Select(s => new { s.ShipDate, PackageCount = s.Packages.Count })
            .ToListAsync(cancellationToken);
        var boxCount = shipments.Sum(s => s.PackageCount);
        var shipDate = shipments.Count > 0
            ? shipments.Max(s => s.ShipDate)
            : order.RequestedShipDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var bytes = await packingListPdfGenerator.GenerateBytesAsync(order, shipDate, boxCount, cancellationToken);
        return new SalesOrderPackingListPdf(order.OrderNumber, bytes);
    }

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
            input.BillingNotes,
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

        SyncLineOutsourcing(order, lines, input.Lines, new Dictionary<Guid, OutsourcedItem>(), new HashSet<Guid>(), userId, now);
        SyncCharges(order, input.Charges, userId, now);
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
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Charges)
            .AsSplitQuery()
            .SingleAsync(e => e.Id == input.EstimateId, cancellationToken);

        if (estimate.Status is not Domain.Enums.EstimateStatus.Won)
        {
            throw new InvalidOperationException("Only won estimates can be converted to a sales order.");
        }

        // Cancelled orders don't block a re-convert — that's the recovery path for a misentered order.
        var existing = await db.SalesOrders.AnyAsync(
            o => o.SourceEstimateId == input.EstimateId && o.Status != SalesOrderStatus.Cancelled,
            cancellationToken);
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
            input.BillingNotes,
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

            // Snapshot the spec the customer was quoted, pinning the die the line's die-cut row was
            // quoted with (falling back to the product's die) + the product's artwork.
            var spec = estimateLine.ToLabelSpec(
                EstimateCalculationMapper.ResolveDieId(estimateLine.FinishingOperationsJson) ?? product.DieId,
                product.ArtworkFilePath);

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

            // Carry the outsourcing quote onto the order: vendor details from the line, cost from the
            // quantity tier that was picked (a different quantity than quoted carries no cost — enter it on the order).
            if (estimateLine.OutsourceDetails is { } outsourceDetails)
            {
                var pickedBreak = estimateLine.QuantityBreaks.FirstOrDefault(q => q.Quantity == lineInput.Quantity);
                db.OutsourcedItems.Add(OutsourcedItem.CreateForLine(
                    Guid.NewGuid(), order.Id, orderLine.Id, outsourceDetails, pickedBreak?.OutsourceCost, userId, now));
            }
        }

        order.ReplaceLines(orderLines);

        var estimateCharges = estimate.Charges.ToDictionary(c => c.Id);
        var chargeNumber = 1;
        var orderCharges = new List<SalesOrderCharge>();
        foreach (var chargeInput in (input.Charges ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Description)))
        {
            var charge = SalesOrderCharge.Create(
                Guid.NewGuid(),
                order.Id,
                chargeNumber++,
                chargeInput.Description,
                chargeInput.EstimateChargeId,
                Math.Max(1, chargeInput.Quantity),
                chargeInput.UnitPrice,
                userId,
                now);
            orderCharges.Add(charge);
            db.SalesOrderCharges.Add(charge);

            if (chargeInput.EstimateChargeId is { } estimateChargeId
                && estimateCharges.TryGetValue(estimateChargeId, out var estimateCharge)
                && estimateCharge.OutsourceDetails is { } chargeOutsource)
            {
                db.OutsourcedItems.Add(OutsourcedItem.CreateForCharge(
                    Guid.NewGuid(), order.Id, charge.Id, chargeOutsource, estimateCharge.OutsourceCost, userId, now));
            }
        }

        order.ReplaceCharges(orderCharges);
        await db.SaveChangesAsync(cancellationToken);
        await invoiceService.CreateFromSalesOrderAsync(order.Id, cancellationToken);
        return order;
    }

    public async Task<Invoices.InvoiceSyncResult> UpdateAsync(
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
            .Include(o => o.Charges).ThenInclude(c => c.OutsourcedItem).ThenInclude(i => i!.Receipts)
            .AsSplitQuery()
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
            input.BillingNotes,
            input.ShippingMethodId,
            input.ShippingCost,
            input.ShippingAddress,
            userId,
            now);
        // Outsourced-item tracking hangs off the line id, which BuildLines preserves (EF folds the
        // remove + re-add of the same key into an update), so it survives the rebuild below.
        var existingItemsByLine = await LoadLineItemsAsync(order.Id, cancellationToken);
        var lineIdsWithJobs = await LoadLineIdsWithJobsAsync(order.Lines.Select(l => l.Id), cancellationToken);

        db.SalesOrderLines.RemoveRange(order.Lines);
        var seedSpecs = await LoadProductSeedSpecsAsync(input.Lines, cancellationToken);
        var lines = BuildLines(order.Id, input.Lines, seedSpecs, userId, now);
        order.ReplaceLines(lines);
        foreach (var line in lines)
        {
            db.SalesOrderLines.Add(line);
        }

        SyncLineOutsourcing(order, lines, input.Lines, existingItemsByLine, lineIdsWithJobs, userId, now);
        SyncCharges(order, input.Charges, userId, now);
        await db.SaveChangesAsync(cancellationToken);

        // Keep the order's draft invoice in step with the edited prices; sent or exported
        // invoices are left alone and reported back to the caller.
        return await invoiceService.SyncDraftFromSalesOrderAsync(id, cancellationToken);
    }

    /// <summary>
    /// The explicit "unlocked" edit path for orders already in production. Lines are updated
    /// in place (matched by id) so job references stay intact, and line changes are propagated
    /// to the linked jobs: spec edits via <see cref="Job.SetSpec"/>, quantity changes via
    /// <see cref="Job.UpdateOrderedQuantity"/>. Lines can be added; a line can only be removed
    /// while no job references it. Products on job-linked lines cannot change.
    /// </summary>
    public async Task<Invoices.InvoiceSyncResult> UpdateInProductionAsync(
        Guid id,
        SalesOrderFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        ValidateLines(input.Lines);

        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .Include(o => o.Charges).ThenInclude(c => c.OutsourcedItem).ThenInclude(i => i!.Receipts)
            .AsSplitQuery()
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
            input.BillingNotes,
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
        var existingItemsByLine = await LoadLineItemsAsync(order.Id, cancellationToken);

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

        SyncLineOutsourcing(
            order,
            order.Lines.ToList(),
            input.Lines,
            existingItemsByLine,
            jobsByLine.Where(g => g.Any()).Select(g => g.Key).ToHashSet(),
            userId,
            now);

        // Charges never have jobs; they are matched by id so outsourced-item tracking survives.
        SyncCharges(order, input.Charges, userId, now);

        // Spec/quantity changes invalidate the plan minutes on pending operations.
        foreach (var jobId in replanJobIds)
        {
            await jobService.RecomputePlannedMinutesAsync(jobId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Keep the order's draft invoice in step with the edited prices; sent or exported
        // invoices are left alone and reported back to the caller.
        return await invoiceService.SyncDraftFromSalesOrderAsync(id, cancellationToken);
    }

    /// <summary>
    /// Copies an order into a fresh Open order for reordering: same customer, sales rep, lines
    /// (product, spec, quantity, price), charges, shipping, and notes. The PO number, requested
    /// ship date, and estimate references are deliberately not carried over — they belong to the
    /// original transaction. A draft invoice is generated like on any new order.
    /// </summary>
    public async Task<SalesOrder> CopyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var source = await db.SalesOrders
            .Include(o => o.Lines).ThenInclude(l => l.OutsourcedItem)
            .Include(o => o.Charges).ThenInclude(c => c.OutsourcedItem)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Sales order not found.");

        var orderNumber = await documentNumbers.NextSalesOrderNumberAsync(cancellationToken);
        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(),
            orderNumber,
            source.CustomerId,
            sourceEstimateId: null,
            source.SalesRepId,
            customerPoNumber: null,
            now,
            requestedShipDate: null,
            source.Notes,
            source.BillingNotes,
            source.ShippingMethodId,
            source.ShippingCost,
            source.ShippingAddress,
            userId,
            now);
        db.SalesOrders.Add(order);

        var sourceLines = source.Lines.OrderBy(l => l.LineNumber).ToList();
        var lines = sourceLines.Select((line, index) =>
            SalesOrderLine.Create(
                Guid.NewGuid(),
                order.Id,
                index + 1,
                line.ProductId,
                line.Description,
                sourceEstimateLineId: null,
                line.Quantity,
                line.UnitPrice,
                line.LineNotes,
                // Spec is an owned entity, so each line needs its own instance.
                line.Spec is null ? null : line.Spec with { },
                userId,
                now)).ToList();
        order.ReplaceLines(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            db.SalesOrderLines.Add(lines[i]);
            // The vendor deal is copied (who/quote/cost/notes); tracking starts fresh on the new order.
            if (sourceLines[i].OutsourcedItem is { } lineItem)
            {
                db.OutsourcedItems.Add(OutsourcedItem.CreateForLine(
                    Guid.NewGuid(), order.Id, lines[i].Id, lineItem.Details, lineItem.VendorCost, userId, now));
            }
        }

        var sourceCharges = source.Charges.OrderBy(c => c.LineNumber).ToList();
        var charges = sourceCharges.Select((charge, index) =>
            SalesOrderCharge.Create(
                Guid.NewGuid(),
                order.Id,
                index + 1,
                charge.Description,
                sourceEstimateChargeId: null,
                charge.Quantity,
                charge.UnitPrice,
                userId,
                now)).ToList();
        order.ReplaceCharges(charges);
        for (var i = 0; i < charges.Count; i++)
        {
            db.SalesOrderCharges.Add(charges[i]);
            if (sourceCharges[i].OutsourcedItem is { } chargeItem)
            {
                db.OutsourcedItems.Add(OutsourcedItem.CreateForCharge(
                    Guid.NewGuid(), order.Id, charges[i].Id, chargeItem.Details, chargeItem.VendorCost, userId, now));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await invoiceService.CreateFromSalesOrderAsync(order.Id, cancellationToken);
        return order;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .Include(o => o.Charges)
            .AsSplitQuery()
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
        db.SalesOrderCharges.RemoveRange(order.Charges);
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

        if (lines.Any(l => l.ProductId == Guid.Empty))
        {
            throw new InvalidOperationException("Select a product on every line item.");
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

    /// <summary>
    /// Brings the order's charge rows in line with the form: rows are matched by id (so an outsourced
    /// charge keeps its vendor tracking across saves), missing rows are removed, new rows appended.
    /// Charges have no jobs, so this is safe in any editable status.
    /// </summary>
    private void SyncCharges(
        SalesOrder order,
        IReadOnlyList<SalesOrderChargeInput>? inputs,
        Guid userId,
        DateTime now)
    {
        var existing = order.Charges.ToDictionary(c => c.Id);
        var wanted = (inputs ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Description)).ToList();
        var keptIds = wanted.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).ToHashSet();

        foreach (var removed in existing.Values.Where(c => !keptIds.Contains(c.Id)).ToList())
        {
            if (removed.OutsourcedItem is { CanBeRemoved: false })
            {
                throw new InvalidOperationException(
                    $"\"{removed.Description}\" has already been sent to or received from the vendor and cannot be removed.");
            }

            db.SalesOrderCharges.Remove(removed);
        }

        // New rows number after every row that has ever been on the order, so a remove + add in the
        // same save never collides on the (order, line number) index.
        var nextNumber = existing.Values.Select(c => c.LineNumber).DefaultIfEmpty(0).Max() + 1;
        var charges = new List<SalesOrderCharge>();
        foreach (var input in wanted)
        {
            SalesOrderCharge charge;
            if (input.Id is { } chargeId && existing.TryGetValue(chargeId, out var current))
            {
                current.Update(input.Description, Math.Max(1, input.Quantity), input.UnitPrice, userId, now);
                charge = current;
            }
            else
            {
                charge = SalesOrderCharge.Create(
                    Guid.NewGuid(),
                    order.Id,
                    nextNumber++,
                    input.Description,
                    input.SourceEstimateChargeId,
                    Math.Max(1, input.Quantity),
                    input.UnitPrice,
                    userId,
                    now);
                db.SalesOrderCharges.Add(charge);
            }

            charges.Add(charge);
            SyncOutsourcedItem(order, charge.OutsourcedItem, input.Outsource, hasJob: false, $"\"{charge.Description}\"",
                () => OutsourcedItem.CreateForCharge(Guid.NewGuid(), order.Id, charge.Id, input.Outsource!.Details, input.Outsource.VendorCost, userId, now),
                userId, now);
        }

        order.ReplaceCharges(charges);
    }

    /// <summary>
    /// Creates, updates, or removes the outsourced item for each order line to match the form.
    /// Lines are matched to their inputs by id (new lines: by position). A line whose item has been
    /// sent to the vendor, or whose job was routed to the vendor, cannot be switched back in-house.
    /// </summary>
    private void SyncLineOutsourcing(
        SalesOrder order,
        IReadOnlyList<SalesOrderLine> lines,
        IReadOnlyList<SalesOrderLineInput> inputs,
        IReadOnlyDictionary<Guid, OutsourcedItem> existingItemsByLine,
        IReadOnlySet<Guid> lineIdsWithJobs,
        Guid userId,
        DateTime now)
    {
        var inputsById = inputs.Where(i => i.Id.HasValue).ToDictionary(i => i.Id!.Value);
        var newInputs = new Queue<SalesOrderLineInput>(inputs.Where(i => !i.Id.HasValue));

        foreach (var line in lines)
        {
            if (!inputsById.TryGetValue(line.Id, out var input))
            {
                if (!newInputs.TryDequeue(out input))
                {
                    continue;
                }
            }

            var existing = line.OutsourcedItem ?? existingItemsByLine.GetValueOrDefault(line.Id);
            SyncOutsourcedItem(order, existing, input.Outsource, lineIdsWithJobs.Contains(line.Id), $"Line {line.LineNumber}",
                () => OutsourcedItem.CreateForLine(Guid.NewGuid(), order.Id, line.Id, input.Outsource!.Details, input.Outsource.VendorCost, userId, now),
                userId, now);
        }

        // Lines dropped from the order take their item with them (cascade) — unless the vendor is already involved.
        var keptLineIds = lines.Select(l => l.Id).ToHashSet();
        foreach (var orphan in existingItemsByLine.Where(kv => !keptLineIds.Contains(kv.Key)).Select(kv => kv.Value))
        {
            if (!orphan.CanBeRemoved)
            {
                throw new InvalidOperationException(
                    "An outsourced line that has already been sent to or received from the vendor cannot be removed.");
            }
        }
    }

    private void SyncOutsourcedItem(
        SalesOrder order,
        OutsourcedItem? existing,
        OutsourceItemInput? input,
        bool hasJob,
        string label,
        Func<OutsourcedItem> create,
        Guid userId,
        DateTime now)
    {
        if (input is not null)
        {
            if (existing is null)
            {
                if (hasJob)
                {
                    throw new InvalidOperationException(
                        $"{label} already has a production job and cannot be switched to outsourced. Cancel the job first or add a new line.");
                }

                db.OutsourcedItems.Add(create());
            }
            else
            {
                existing.UpdateDetails(input.Details, input.VendorCost, userId, now);
            }
        }
        else if (existing is not null)
        {
            if (!existing.CanBeRemoved)
            {
                throw new InvalidOperationException(
                    $"{label} has already been sent to or received from the vendor and cannot be switched back to in-house.");
            }

            if (hasJob)
            {
                throw new InvalidOperationException(
                    $"{label} has a job routed to the vendor and cannot be switched back to in-house. Cancel the job first or add a new line.");
            }

            db.OutsourcedItems.Remove(existing);
        }
    }

    private async Task<Dictionary<Guid, OutsourcedItem>> LoadLineItemsAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        await db.OutsourcedItems
            .Include(o => o.Receipts)
            .Where(o => o.SalesOrderId == salesOrderId && o.SalesOrderLineId != null)
            .ToDictionaryAsync(o => o.SalesOrderLineId!.Value, cancellationToken);

    private async Task<HashSet<Guid>> LoadLineIdsWithJobsAsync(IEnumerable<Guid> lineIds, CancellationToken cancellationToken)
    {
        var ids = lineIds.ToList();
        return (await db.Jobs.AsNoTracking()
                .Where(j => ids.Contains(j.SalesOrderLineId) && j.Status != JobStatus.Closed)
                .Select(j => j.SalesOrderLineId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task<Dictionary<Guid, LabelSpec>> LoadProductSeedSpecsAsync(
        IReadOnlyList<SalesOrderLineInput> lines,
        CancellationToken cancellationToken)
    {
        var productIds = lines.Select(l => l.ProductId).Where(id => id != Guid.Empty).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        // Products don't hold a layflat, so shrink-film substrates seed it from the stock default.
        var substrateIds = products.Select(p => p.SubstrateId).Distinct().ToList();
        var shrinkLayflats = await db.Stocks.AsNoTracking()
            .Where(s => substrateIds.Contains(s.Id) && s.StockType == StockType.Shrink)
            .ToDictionaryAsync(s => s.Id, s => s.ShrinkLayflatIn, cancellationToken);

        return products.ToDictionary(
            p => p.Id,
            p => shrinkLayflats.TryGetValue(p.SubstrateId, out var layflat)
                ? p.ToLabelSpec() with { ShrinkLayflatIn = layflat }
                : p.ToLabelSpec());
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
