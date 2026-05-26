using System.Text.Json;
using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Models;
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
    DateOnly? ValidUntilDate);

public record EstimateDetail(
    Estimate Estimate,
    Customer Customer,
    IReadOnlyList<EstimateRevision> Revisions,
    Guid? SalesOrderId);

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
    IEmailSender emailSender)
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

        query = sort switch
        {
            "number" => query.OrderBy(e => e.EstimateNumber),
            "number_desc" => query.OrderByDescending(e => e.EstimateNumber),
            "customer" => query.OrderBy(e => e.Customer.Name),
            "status" => query.OrderBy(e => e.Status).ThenByDescending(e => e.CreatedAt),
            "valid" => query.OrderBy(e => e.ValidUntilDate),
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
                    .Sum(t => t!.Value);

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
                    e.ValidUntilDate);
            })
            .ToList();

        return new PagedResult<EstimateListItem>(items, total, page, pageSize);
    }

    public async Task<EstimateDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var estimate = await db.Estimates
            .Include(e => e.Customer).ThenInclude(c => c.Addresses)
            .Include(e => e.Lines).ThenInclude(l => l.Substrate)
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Revisions)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (estimate is null)
        {
            return null;
        }

        var salesOrderId = await db.SalesOrders.AsNoTracking()
            .Where(o => o.SourceEstimateId == id)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new EstimateDetail(
            estimate,
            estimate.Customer,
            estimate.Revisions.OrderByDescending(r => r.RevisionNumber).ToList(),
            salesOrderId);
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
        EnsureNoCalculationErrors(calc);

        var estimateNumber = await documentNumbers.NextEstimateNumberAsync(cancellationToken);
        var estimate = Estimate.CreateDraft(
            Guid.NewGuid(),
            estimateNumber,
            input.CustomerId,
            input.SalesRepId,
            input.Notes,
            input.ValidUntilDate,
            userId,
            now);

        AddLines(estimate, input.Lines, calc, userId, now);
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
            .SingleAsync(e => e.Id == id, cancellationToken);

        var calc = await CalculateAsync(input, cancellationToken);
        EnsureNoCalculationErrors(calc);

        estimate.UpdateDraft(input.SalesRepId, input.Notes, input.ValidUntilDate, userId, now);

        // Replace all lines + breaks (simplest path; preserves estimate identity but rewrites lines)
        foreach (var line in estimate.Lines.ToList())
        {
            db.EstimateQuantityBreaks.RemoveRange(line.QuantityBreaks);
        }
        db.EstimateLines.RemoveRange(estimate.Lines);
        estimate.ReplaceLines([]);

        AddLines(estimate, input.Lines, calc, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SendAsync(Guid id, bool sendEmail, string? emailTo, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var detail = await GetDetailAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Estimate not found.");

        var tracked = await db.Estimates.Include(e => e.Lines).SingleAsync(e => e.Id == id, cancellationToken);
        var pdfPath = await pdfGenerator.GenerateAsync(detail, cancellationToken);
        tracked.MarkSent(pdfPath, userId, now);
        await db.SaveChangesAsync(cancellationToken);

        if (sendEmail && !string.IsNullOrWhiteSpace(emailTo))
        {
            await emailSender.SendAsync(
                emailTo,
                $"Estimate {tracked.EstimateNumber}",
                $"Please find attached estimate {tracked.EstimateNumber}.",
                [pdfPath],
                cancellationToken);
        }
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

    public async Task CreateRevisionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var estimate = await db.Estimates
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Include(e => e.Revisions)
            .SingleAsync(e => e.Id == id, cancellationToken);

        var snapshot = JsonSerializer.Serialize(new
        {
            estimate.EstimateNumber,
            estimate.RevisionNumber,
            estimate.Status,
            estimate.Notes,
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
                l.WhiteInkUsed,
                l.FinishingOperationsJson,
                l.SetupWasteImpressions,
                l.RunningWastePct,
                l.LineNotes,
                QuantityBreaks = l.QuantityBreaks.Select(q => new
                {
                    q.Quantity,
                    q.UnitPrice,
                    q.TotalPrice,
                    q.CalculatedCost,
                    q.MarginPct
                })
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

        if (!string.IsNullOrWhiteSpace(estimate.PdfFilePath) && File.Exists(estimate.PdfFilePath))
        {
            File.Delete(estimate.PdfFilePath);
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
                input.WhiteInkUsed,
                EstimateCalculationMapper.SerializeFinishingOperations(input.FinishingOperations),
                input.SetupWasteImpressions,
                input.RunningWastePct,
                input.LineNotes,
                input.MarkupPctOverride,
                input.MaxLabelsAcrossOverride,
                input.LabelOrientationOverride,
                userId,
                now);

            var lineCalc = calc.Lines.FirstOrDefault(l => l.LineIndex == i);
            if (lineCalc is not null)
            {
                var breaks = lineCalc.QuantityBreaks.Select(b =>
                    EstimateQuantityBreak.Create(
                        Guid.NewGuid(),
                        line.Id,
                        b.Quantity,
                        b.UnitPrice,
                        b.TotalPrice,
                        b.TotalCost,
                        b.MarginPct,
                        EstimateCalculationMapper.SerializeCostBreakdown(b.CostBreakdown),
                        userId,
                        now)).ToList();
                line.ReplaceQuantityBreaks(breaks);
                foreach (var brk in breaks)
                {
                    db.EstimateQuantityBreaks.Add(brk);
                }
            }

            lines.Add(line);
            db.EstimateLines.Add(line);
        }

        estimate.ReplaceLines(lines);
    }

    private static void EnsureNoCalculationErrors(EstimateCalculationResponse calc)
    {
        var errors = calc.Lines
            .Where(l => l.Errors.Count > 0)
            .SelectMany(l => l.Errors.Select(e => $"Line {l.LineIndex + 1}: {e}"))
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors));
        }
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
