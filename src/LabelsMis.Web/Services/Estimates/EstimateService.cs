using System.Text.Json;
using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.Pdfs;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Estimates;

public record EstimateListItem(
    Guid Id,
    string EstimateNumber,
    int RevisionNumber,
    string CustomerName,
    string SummaryDescription,
    int LineCount,
    EstimateStatus Status,
    decimal? HighestQtyTotal,
    DateTime CreatedAt,
    DateOnly? ValidUntilDate,
    bool IsSentToCustomer = true);

public record EstimateDetail(
    Estimate Estimate,
    Customer Customer,
    IReadOnlyList<EstimateRevision> Revisions,
    Guid? SalesOrderId,
    string? SalesOrderNumber,
    string? SalesRepName = null);

public class EstimateOptions
{
    public const string SectionName = "Estimates";
    public string PdfStoragePath { get; set; } = "./data/pdfs/estimates";
    public string TermsText { get; set; } = "Prices valid for 30 days. Subject to credit approval.";
    public string ShopName { get; set; } = "Labels MIS Print Shop";
}

public class EstimateService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    EstimateCalculationMapper calculationMapper,
    EstimatingService estimatingService,
    EstimatePdfGenerator pdfGenerator,
    TempPdfStorage pdfStorage,
    IEmailSender emailSender,
    SalesOrders.SalesOrderService salesOrderService)
{
    public async Task<PagedResult<EstimateListItem>> ListAsync(
        string? search,
        EstimateStatus? status,
        Guid? customerId,
        Guid? salesRepId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Estimates.AsNoTracking()
            .Include(e => e.Customer)
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Charges)
            .AsSplitQuery()
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(e => e.CustomerId == customerId.Value);
        }

        if (salesRepId.HasValue)
        {
            query = query.Where(e => e.SalesRepId == salesRepId.Value);
        }

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(e => e.CreatedAt >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(e => e.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(e =>
                e.EstimateNumber.ToUpper().Contains(term)
                || e.Customer.Name.ToUpper().Contains(term)
                || e.Lines.Any(l => l.ProductDescription.ToUpper().Contains(term)));
        }

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "number" => query.OrderByDir(desc, e => e.EstimateNumber),
            "customer" => query.OrderByDir(desc, e => e.Customer.Name),
            "status" => query.OrderByDir(desc, e => e.Status).ThenByDescending(e => e.CreatedAt),
            "created" => query.OrderByDir(desc, e => e.CreatedAt),
            "valid" => query.OrderByDir(desc, e => e.ValidUntilDate),
            _ => query.OrderByDescending(e => e.CreatedAt)
        };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(e =>
            {
                var firstLine = e.Lines.OrderBy(l => l.LineNumber).FirstOrDefault();
                var summary = firstLine?.ProductDescription ?? "(no lines)";
                if (e.Lines.Count > 1)
                {
                    summary += $" (+{e.Lines.Count - 1} more)";
                }

                decimal? highest = e.Lines
                    .SelectMany(l => l.QuantityBreaks)
                    .GroupBy(_ => 0)
                    .Select(g => g.OrderByDescending(q => q.Quantity).First().TotalPrice)
                    .FirstOrDefault();

                decimal? topTotal = e.Lines
                    .Select(l => l.QuantityBreaks.OrderByDescending(q => q.Quantity).Select(q => (decimal?)q.TotalPrice).FirstOrDefault())
                    .Where(t => t.HasValue)
                    .Sum(t => t!.Value)
                    + e.Charges.Sum(c => c.LineTotal);

                return new EstimateListItem(
                    e.Id,
                    e.EstimateNumber,
                    e.RevisionNumber,
                    e.Customer.Name,
                    summary,
                    e.Lines.Count,
                    e.Status,
                    topTotal == 0 ? null : topTotal,
                    e.CreatedAt,
                    e.ValidUntilDate,
                    e.SentAt != null);
            })
            .ToList();

        return new PagedResult<EstimateListItem>(items, page, pageSize, total);
    }

    public async Task<EstimateDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var estimate = await db.Estimates
            .Include(e => e.Customer).ThenInclude(c => c.Addresses)
            .Include(e => e.Customer).ThenInclude(c => c.Contacts)
            .Include(e => e.ShippingMethod)
            .Include(e => e.Lines).ThenInclude(l => l.Substrate)
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Lines).ThenInclude(l => l.OutsourceVendor)
            .Include(e => e.Charges).ThenInclude(c => c.OutsourceVendor)
            .Include(e => e.Revisions)
            .AsSplitQuery()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (estimate is null)
        {
            return null;
        }

        // Cancelled orders don't count as the estimate's linked order — a won estimate whose
        // misentered order was cancelled can be converted again.
        var salesOrder = await db.SalesOrders.AsNoTracking()
            .Where(o => o.SourceEstimateId == id && o.Status != SalesOrderStatus.Cancelled)
            .Select(o => new { o.Id, o.OrderNumber })
            .FirstOrDefaultAsync(cancellationToken);

        string? salesRepName = null;
        if (estimate.SalesRepId is { } repId)
        {
            salesRepName = await db.Users.AsNoTracking()
                .Where(u => u.Id == repId)
                .Select(u => u.Email ?? u.UserName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new EstimateDetail(
            estimate,
            estimate.Customer,
            estimate.Revisions.OrderByDescending(r => r.RevisionNumber).ToList(),
            salesOrder?.Id,
            salesOrder?.OrderNumber,
            salesRepName);
    }

    public async Task<EstimateCalculationResponse> CalculateAsync(
        EstimateFormInput input,
        CancellationToken cancellationToken = default)
    {
        var responses = new List<EstimateLineCalculationResponse>(input.Lines.Count);
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            if (line.SubstrateId == Guid.Empty)
            {
                responses.Add(new EstimateLineCalculationResponse(
                    i,
                    [],
                    null,
                    null,
                    [],
                    ["Select a substrate."],
                    line.MarkupPctOverride ?? 0m));
                continue;
            }

            try
            {
                var request = await calculationMapper.BuildRequestAsync(input.CustomerId, line, cancellationToken);
                var result = estimatingService.Calculate(request);
                var layout = new ImpositionLayoutView(
                    request.PressWebWidthIn,
                    request.PressMaxImageWidthIn,
                    request.PressMaxImageLengthIn,
                    request.PressEdgeMarginIn,
                    request.StockWidthIn,
                    result.Imposition?.EffectiveLabelAcrossIn ?? request.LabelAcrossIn,
                    result.Imposition?.EffectiveLabelAroundIn ?? request.LabelAroundIn,
                    request.GutterAcrossIn,
                    request.GutterAroundIn);
                responses.Add(new EstimateLineCalculationResponse(
                    i,
                    result.QuantityBreaks,
                    result.Imposition,
                    layout,
                    result.Warnings,
                    result.Errors,
                    request.CustomerMarkupPct));
            }
            catch (Exception ex)
            {
                responses.Add(new EstimateLineCalculationResponse(
                    i,
                    [],
                    null,
                    null,
                    [],
                    [ex.Message],
                    line.MarkupPctOverride ?? 0m));
            }
        }
        return new EstimateCalculationResponse(responses);
    }

    public async Task<Estimate> CreateDraftAsync(
        EstimateFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var calc = await CalculateAsync(input, cancellationToken);
        EnsureNoCalculationErrors(calc, input.Lines);

        await ValidateShippingMethodAsync(input.ShippingMethodId, cancellationToken);
        var estimateNumber = await documentNumbers.NextEstimateNumberAsync(cancellationToken);
        var estimate = Estimate.CreateDraft(
            Guid.NewGuid(),
            estimateNumber,
            input.CustomerId,
            input.SalesRepId,
            input.Notes,
            input.BillingNotes,
            input.ValidUntilDate,
            input.ShippingMethodId,
            input.ShippingCost,
            input.ShippingAddress,
            userId,
            now);

        AddLines(estimate, input.Lines, calc, userId, now);
        AddCharges(estimate, input.Charges, userId, now);
        db.Estimates.Add(estimate);
        await db.SaveChangesAsync(cancellationToken);
        return estimate;
    }

    public async Task UpdateDraftAsync(
        Guid id,
        EstimateFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var estimate = await db.Estimates
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Charges)
            .AsSplitQuery()
            .SingleAsync(e => e.Id == id, cancellationToken);

        var calc = await CalculateAsync(input, cancellationToken);
        EnsureNoCalculationErrors(calc, input.Lines);

        await ValidateShippingMethodAsync(input.ShippingMethodId, cancellationToken);
        estimate.UpdateDraft(
            input.SalesRepId,
            input.Notes,
            input.BillingNotes,
            input.ValidUntilDate,
            input.ShippingMethodId,
            input.ShippingCost,
            input.ShippingAddress,
            userId,
            now);

        // Replace all lines + breaks (simplest path; preserves estimate identity but rewrites lines)
        foreach (var line in estimate.Lines.ToList())
        {
            db.EstimateQuantityBreaks.RemoveRange(line.QuantityBreaks);
        }
        db.EstimateLines.RemoveRange(estimate.Lines);
        estimate.ReplaceLines([]);
        db.EstimateCharges.RemoveRange(estimate.Charges);
        estimate.ReplaceCharges([]);

        AddLines(estimate, input.Lines, calc, userId, now);
        AddCharges(estimate, input.Charges, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SendAsync(
        Guid id,
        string? emailTo,
        string? subject,
        string? body,
        bool includePdf,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var detail = await GetDetailAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Estimate not found.");

        var tracked = await db.Estimates.Include(e => e.Lines).SingleAsync(e => e.Id == id, cancellationToken);
        var pdfPath = await pdfGenerator.GenerateAsync(detail, cancellationToken);
        tracked.MarkSent(pdfPath, userId, now);
        if (!string.IsNullOrWhiteSpace(emailTo))
        {
            tracked.SetContactEmail(emailTo, userId, now);
        }
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(emailTo))
        {
            await emailSender.SendAsync(
                emailTo,
                DefaultIfBlank(subject, $"Estimate {tracked.EstimateNumber}"),
                DefaultIfBlank(body, $"Please find attached estimate {tracked.EstimateNumber}."),
                includePdf ? [pdfPath] : null,
                cancellationToken);
        }
    }

    public async Task ResendAsync(
        Guid id,
        string? emailTo,
        string? subject,
        string? body,
        bool includePdf,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        if (string.IsNullOrWhiteSpace(emailTo))
        {
            throw new InvalidOperationException("An email address is required to resend the estimate.");
        }

        var detail = await GetDetailAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Estimate not found.");
        var tracked = await db.Estimates.SingleAsync(e => e.Id == id, cancellationToken);

        IReadOnlyList<string>? attachments = null;
        if (includePdf)
        {
            // Reuse the existing PDF when it is still in storage; otherwise regenerate it.
            var pdfPath = !string.IsNullOrWhiteSpace(tracked.PdfFilePath)
                    && await pdfStorage.ExistsAsync(tracked.PdfFilePath, cancellationToken)
                ? tracked.PdfFilePath!
                : await pdfGenerator.GenerateAsync(detail, cancellationToken);
            attachments = [pdfPath];
        }

        await emailSender.SendAsync(
            emailTo,
            DefaultIfBlank(subject, $"Estimate {tracked.EstimateNumber}"),
            DefaultIfBlank(body, $"Please find attached estimate {tracked.EstimateNumber}."),
            attachments,
            cancellationToken);

        var sentNow = DateTime.UtcNow;
        tracked.SetContactEmail(emailTo, userId, sentNow);
        // A won-without-sending estimate that is emailed later counts as sent from here on.
        tracked.RecordSentToCustomer(attachments?.FirstOrDefault(), userId, sentNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string DefaultIfBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>Renders the official PDF for a saved estimate as bytes (no persistence).</summary>
    public async Task<byte[]?> RenderPdfBytesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await GetDetailAsync(id, cancellationToken);
        return detail is null ? null : await pdfGenerator.GenerateBytesAsync(detail, cancellationToken);
    }

    /// <summary>Renders a preview PDF from in-progress (unsaved) form state.</summary>
    public async Task<byte[]> GeneratePreviewAsync(
        EstimateFormInput input,
        string? estimateNumber,
        int revisionNumber,
        CancellationToken cancellationToken = default)
    {
        var calc = await CalculateAsync(input, cancellationToken);

        Customer? customer = null;
        if (input.CustomerId != Guid.Empty)
        {
            customer = await db.Customers.AsNoTracking()
                .Include(c => c.Addresses)
                .FirstOrDefaultAsync(c => c.Id == input.CustomerId, cancellationToken);
        }

        var substrateIds = input.Lines.Select(l => l.SubstrateId).Where(id => id != Guid.Empty).Distinct().ToList();
        var substrates = await db.Stocks.AsNoTracking()
            .Where(s => substrateIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Description, cancellationToken);

        string? shippingName = null;
        if (input.ShippingMethodId is { } smId && smId != Guid.Empty)
        {
            shippingName = await db.ShippingMethods.AsNoTracking()
                .Where(m => m.Id == smId).Select(m => m.Name).FirstOrDefaultAsync(cancellationToken);
        }

        string? salesRepName = null;
        if (input.SalesRepId is { } repId)
        {
            salesRepName = await db.Users.AsNoTracking()
                .Where(u => u.Id == repId).Select(u => u.Email ?? u.UserName).FirstOrDefaultAsync(cancellationToken);
        }

        var billing = customer?.Addresses.FirstOrDefault(a => a.IsDefault) ?? customer?.Addresses.FirstOrDefault();
        var ship = input.ShippingAddress;

        var lines = new List<EstimatePdfLine>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var l = input.Lines[i];
            var lineCalc = calc.Lines.FirstOrDefault(c => c.LineIndex == i);
            var previewQuantities = (lineCalc?.QuantityBreaks.Select(b => b.Quantity).ToList() is { Count: > 0 } calcQty)
                ? calcQty
                : l.Outsource is not null ? l.Quantities.ToList() : [];
            var breaks = previewQuantities
                .OrderBy(q => q)
                .Select(q =>
                {
                    // Outsourced lines quote the entered final price; everything else the calculator's.
                    if (l.Outsource is not null && l.Outsource.Pricing.TryGetValue(q, out var op))
                    {
                        return new EstimatePdfBreak(q, op.FinalPrice / q, op.FinalPrice);
                    }

                    var b = lineCalc?.QuantityBreaks.First(x => x.Quantity == q);
                    return new EstimatePdfBreak(q, b?.UnitPrice ?? 0m, b?.TotalPrice ?? 0m);
                })
                .ToList();
            lines.Add(new EstimatePdfLine(
                i + 1,
                string.IsNullOrWhiteSpace(l.ProductDescription) ? "Untitled line" : l.ProductDescription,
                l.LabelAcrossIn,
                l.LabelAroundIn,
                substrates.TryGetValue(l.SubstrateId, out var desc) ? desc : "—",
                l.InkSet.ToString(),
                breaks));
        }

        var model = new EstimatePdfModel(
            string.IsNullOrWhiteSpace(estimateNumber) ? "DRAFT" : estimateNumber,
            revisionNumber <= 0 ? 1 : revisionNumber,
            DateTime.UtcNow,
            input.ValidUntilDate,
            customer?.Name ?? "—",
            billing is null ? null : new EstimatePdfAddress(null, billing.Street1, billing.Street2, billing.City, billing.State, billing.Zip),
            ship.HasAddress ? new EstimatePdfAddress(ship.RecipientName, ship.Street1, ship.Street2, ship.City, ship.State, ship.Zip) : null,
            lines,
            shippingName,
            input.ShippingCost,
            (input.ShippingMethodId is { } m && m != Guid.Empty) || input.ShippingCost > 0,
            salesRepName,
            (input.Charges ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Description))
                .Select(c => new EstimatePdfCharge(
                    c.Description, Math.Max(1, c.Quantity), c.UnitPrice, c.UnitPrice * Math.Max(1, c.Quantity)))
                .ToList());

        return await pdfGenerator.GenerateBytesAsync(model, cancellationToken);
    }

    public async Task MarkWonAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var estimate = await db.Estimates.Include(e => e.Lines).SingleAsync(e => e.Id == id, cancellationToken);
        estimate.MarkWon(userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkLostAsync(Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var estimate = await db.Estimates.SingleAsync(e => e.Id == id, cancellationToken);
        estimate.MarkLost(reason, userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Admin cleanup for a misentered estimate (typically one already marked won): cancels the
    /// estimate and cascades to every sales order created from it — each order is cancelled, its
    /// jobs are closed, and its unpaid invoices are voided.
    /// </summary>
    public async Task CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var estimate = await db.Estimates.SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Estimate not found.");

        estimate.Cancel(userId, DateTime.UtcNow);

        var orderIds = await db.SalesOrders
            .Where(o => o.SourceEstimateId == id && o.Status != SalesOrderStatus.Cancelled)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        foreach (var orderId in orderIds)
        {
            await salesOrderService.CancelAsync(orderId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateRevisionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var estimate = await db.Estimates
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Charges)
            .Include(e => e.Revisions)
            .AsSplitQuery()
            .SingleAsync(e => e.Id == id, cancellationToken);

        var snapshot = JsonSerializer.Serialize(new
        {
            estimate.EstimateNumber,
            estimate.RevisionNumber,
            estimate.Status,
            estimate.Notes,
            estimate.BillingNotes,
            estimate.ValidUntilDate,
            Lines = estimate.Lines.OrderBy(l => l.LineNumber).Select(l => new
            {
                l.LineNumber,
                l.ProductDescription,
                l.LabelAcrossIn,
                l.LabelAroundIn,
                l.CornerRadiusIn,
                l.GutterAcrossIn,
                l.GutterAroundIn,
                l.BleedIn,
                l.SubstrateId,
                l.InkSet,
                l.WhiteHits,
                l.WhiteCoveragePct,
                l.SpotsJson,
                l.FinishingOperationsJson,
                l.SetupWasteImpressions,
                l.RunningWastePct,
                l.LineNotes,
                l.IsOutsourced,
                l.OutsourceVendorId,
                l.OutsourceQuoteNumber,
                l.OutsourceExpectedIn,
                l.OutsourcePrivateNotes,
                QuantityBreaks = l.QuantityBreaks.Select(q => new
                {
                    q.Quantity,
                    q.UnitPrice,
                    q.TotalPrice,
                    q.CalculatedCost,
                    q.CalculatedUnitPrice,
                    q.CalculatedTotalPrice,
                    q.OutsourceCost,
                    q.MarginPct,
                    q.MarkupPctOverride
                })
            }),
            Charges = estimate.Charges.OrderBy(c => c.LineNumber).Select(c => new
            {
                c.LineNumber,
                c.Description,
                c.Quantity,
                c.UnitPrice,
                c.LineTotal,
                c.IsOutsourced,
                c.OutsourceVendorId,
                c.OutsourceQuoteNumber,
                c.OutsourceCost,
                c.OutsourceExpectedIn,
                c.OutsourcePrivateNotes
            })
        });

        var revision = estimate.CreateRevisionSnapshot(Guid.NewGuid(), snapshot, userId, now);
        db.EstimateRevisions.Add(revision);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var estimate = await db.Estimates
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Revisions)
            .AsSplitQuery()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Estimate not found.");

        var lineIds = estimate.Lines.Select(l => l.Id).ToList();
        var hasProduct = await db.Products.AnyAsync(p => p.SourceEstimateLineId != null && lineIds.Contains(p.SourceEstimateLineId.Value), cancellationToken);
        if (hasProduct)
        {
            throw new InvalidOperationException("Cannot delete an estimate that has linked products.");
        }
        var hasOrder = await db.SalesOrders.AnyAsync(o => o.SourceEstimateId == id, cancellationToken);
        if (hasOrder)
        {
            throw new InvalidOperationException("Cannot delete an estimate that has a linked sales order.");
        }

        estimate.EnsureCanDelete();

        if (!string.IsNullOrWhiteSpace(estimate.PdfFilePath))
        {
            await pdfStorage.DeleteAsync(estimate.PdfFilePath, cancellationToken);
        }

        db.Estimates.Remove(estimate);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddLines(
        Estimate estimate,
        IReadOnlyList<EstimateLineFormInput> inputs,
        EstimateCalculationResponse calc,
        Guid userId,
        DateTime now)
    {
        if (inputs.Count == 0)
        {
            throw new InvalidOperationException("Estimate must have at least one line.");
        }

        var lines = new List<EstimateLine>(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var line = EstimateLine.Create(
                Guid.NewGuid(),
                estimate.Id,
                i + 1,
                input.SourceProductId,
                input.ProductDescription,
                input.LabelAcrossIn,
                input.LabelAroundIn,
                input.CornerRadiusIn,
                input.GutterAcrossIn,
                input.GutterAroundIn,
                input.BleedIn,
                input.SubstrateId,
                input.InkSet,
                input.WhiteHits,
                input.WhiteCoveragePct,
                EstimateCalculationMapper.SerializeSpots(input.Spots),
                EstimateCalculationMapper.SerializeFinishingOperations(input.FinishingOperations),
                input.SetupWasteImpressions,
                input.RunningWastePct,
                input.LineNotes,
                input.MarkupPctOverride,
                input.MaxLabelsAcrossOverride,
                input.LabelOrientationOverride,
                input.Unwind,
                input.ShrinkLayflatIn,
                userId,
                now);

            line.SetOutsource(input.Outsource?.Details, userId, now);

            var breaks = BuildQuantityBreaks(line, input, calc.Lines.FirstOrDefault(l => l.LineIndex == i), userId, now);
            line.ReplaceQuantityBreaks(breaks);
            foreach (var brk in breaks)
            {
                db.EstimateQuantityBreaks.Add(brk);
            }

            lines.Add(line);
            db.EstimateLines.Add(line);
        }

        estimate.ReplaceLines(lines);
    }

    /// <summary>
    /// The calculator's breaks for a line, with outsourced pricing layered on: an outsourced line
    /// keeps the calculated cost/price for comparison but quotes the entered vendor cost + final
    /// price for every quantity. When the in-house calculation cannot run at all (a label the press
    /// can't print is a classic reason to outsource), the outsourced quantities are still quoted with
    /// zero calculated values.
    /// </summary>
    private static List<EstimateQuantityBreak> BuildQuantityBreaks(
        EstimateLine line,
        EstimateLineFormInput input,
        EstimateLineCalculationResponse? lineCalc,
        Guid userId,
        DateTime now)
    {
        var breaks = new List<EstimateQuantityBreak>();
        var calculated = lineCalc?.QuantityBreaks ?? [];
        var quantities = calculated.Count > 0
            ? calculated.Select(b => b.Quantity).ToList()
            : input.Outsource is not null ? input.Quantities.ToList() : [];

        foreach (var quantity in quantities)
        {
            var b = calculated.FirstOrDefault(c => c.Quantity == quantity);
            var brk = EstimateQuantityBreak.Create(
                Guid.NewGuid(),
                line.Id,
                quantity,
                b?.UnitPrice ?? 0m,
                b?.TotalPrice ?? 0m,
                b?.TotalCost ?? 0m,
                b?.MarginPct ?? 0m,
                input.QuantityMarkupOverrides is not null
                    && input.QuantityMarkupOverrides.TryGetValue(quantity, out var markupOverride)
                    ? markupOverride
                    : null,
                b is null ? "[]" : EstimateCalculationMapper.SerializeCostBreakdown(b.CostBreakdown),
                userId,
                now);

            if (input.Outsource is not null)
            {
                if (!input.Outsource.Pricing.TryGetValue(quantity, out var pricing))
                {
                    throw new InvalidOperationException(
                        $"Line {line.LineNumber}: enter the vendor cost and final price for quantity {quantity:N0}.");
                }

                brk.ApplyOutsourcePricing(pricing.VendorCost, pricing.FinalPrice);
            }

            breaks.Add(brk);
        }

        return breaks;
    }

    private void AddCharges(
        Estimate estimate,
        IReadOnlyList<EstimateChargeFormInput>? inputs,
        Guid userId,
        DateTime now)
    {
        var charges = new List<EstimateCharge>();
        var lineNumber = 1;
        foreach (var input in (inputs ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Description)))
        {
            var charge = EstimateCharge.Create(
                Guid.NewGuid(),
                estimate.Id,
                lineNumber++,
                input.Description,
                Math.Max(1, input.Quantity),
                input.UnitPrice,
                userId,
                now);
            charge.SetOutsource(input.Outsource?.Details, input.Outsource?.VendorCost, userId, now);
            charges.Add(charge);
            db.EstimateCharges.Add(charge);
        }

        estimate.ReplaceCharges(charges);
    }

    /// <summary>Calculation errors block saving — except on outsourced lines, where the in-house
    /// calc is only a comparison and may legitimately fail (e.g. a label the press can't run).</summary>
    private static void EnsureNoCalculationErrors(EstimateCalculationResponse calc, IReadOnlyList<EstimateLineFormInput> lines)
    {
        var errors = calc.Lines
            .Where(l => l.Errors.Count > 0 && lines.ElementAtOrDefault(l.LineIndex)?.Outsource is null)
            .SelectMany(l => l.Errors.Select(e => $"Line {l.LineIndex + 1}: {e}"))
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors));
        }
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
