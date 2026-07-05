using LabelsMis.Domain.Entities;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Tools.Importers;

/// <summary>
/// One-time backfill of the snapshotted <see cref="LabelSpec"/> onto sales-order lines and jobs that
/// predate the LabelSpec refactor. See docs/labelspec-refactor.md §7.
///
/// Resolution order per sales-order line: the line's source estimate line → the order's source
/// estimate matched by product → the product template. Jobs source from their (now-backfilled) sales
/// order line, else the product template.
/// </summary>
public sealed class SpecBackfill : CsvImporterBase
{
    public static async Task<ImportResult> RunAsync(Guid actorId, string? connectionString = null)
    {
        var errors = new List<string>();
        var now = DateTime.UtcNow;
        await using var db = await CreateDbContextAsync(ResolveConnectionString(connectionString));

        // The audit columns FK to AspNetUsers, so attribute the backfill to a real user — the given
        // placeholder id if it exists, otherwise any existing user.
        var actor = await db.Users.AnyAsync(u => u.Id == actorId)
            ? actorId
            : await db.Users.Select(u => u.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty)
        {
            return new ImportResult(0, 0, ["No users exist to attribute the backfill to."]);
        }

        var soFilled = await BackfillSalesOrderLinesAsync(db, actor, now, errors);
        var jobFilled = await BackfillJobsAsync(db, actor, now, errors);

        return new ImportResult(soFilled + jobFilled, 0, errors);
    }

    private static async Task<int> BackfillSalesOrderLinesAsync(
        LabelsMisDbContext db, Guid actorId, DateTime now, List<string> errors)
    {
        // Owned-reference null checks are done in memory to stay portable across EF providers.
        var lines = (await db.SalesOrderLines.Include(l => l.SalesOrder).ToListAsync())
            .Where(l => l.Spec is null)
            .ToList();
        if (lines.Count == 0)
        {
            return 0;
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var directEstimateLineIds = lines
            .Where(l => l.SourceEstimateLineId is not null)
            .Select(l => l.SourceEstimateLineId!.Value)
            .ToList();
        var sourceEstimateIds = lines
            .Where(l => l.SalesOrder.SourceEstimateId is not null)
            .Select(l => l.SalesOrder.SourceEstimateId!.Value)
            .Distinct()
            .ToList();
        var estimateLines = await db.EstimateLines
            .Where(el => directEstimateLineIds.Contains(el.Id) || sourceEstimateIds.Contains(el.EstimateId))
            .ToListAsync();
        var estimateLineById = estimateLines.ToDictionary(el => el.Id);
        var estimateLineByEstimateProduct = estimateLines
            .Where(el => el.SourceProductId is not null)
            .GroupBy(el => (el.EstimateId, el.SourceProductId!.Value))
            .ToDictionary(g => g.Key, g => g.First());

        var filled = 0;
        foreach (var line in lines)
        {
            products.TryGetValue(line.ProductId, out var product);

            EstimateLine? estimateLine = null;
            if (line.SourceEstimateLineId is Guid elId)
            {
                estimateLineById.TryGetValue(elId, out estimateLine);
            }
            if (estimateLine is null && line.SalesOrder.SourceEstimateId is Guid estimateId)
            {
                estimateLineByEstimateProduct.TryGetValue((estimateId, line.ProductId), out estimateLine);
            }

            var spec = estimateLine?.ToLabelSpec(product?.DieId, product?.ArtworkFilePath)
                ?? product?.ToLabelSpec();
            if (spec is null)
            {
                errors.Add($"SalesOrderLine {line.Id}: no estimate line or product to source spec from.");
                continue;
            }

            line.SetSpec(spec, actorId, now);
            filled++;
        }

        await db.SaveChangesAsync();
        return filled;
    }

    private static async Task<int> BackfillJobsAsync(
        LabelsMisDbContext db, Guid actorId, DateTime now, List<string> errors)
    {
        var jobs = (await db.Jobs.Include(j => j.SalesOrderLine).ToListAsync())
            .Where(j => j.Spec is null)
            .ToList();
        if (jobs.Count == 0)
        {
            return 0;
        }

        var productIds = jobs.Select(j => j.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var filled = 0;
        foreach (var job in jobs)
        {
            var spec = job.SalesOrderLine?.Spec
                ?? (products.TryGetValue(job.ProductId, out var product) ? product.ToLabelSpec() : null);
            if (spec is null)
            {
                errors.Add($"Job {job.Id}: no sales-order-line spec or product to source spec from.");
                continue;
            }

            job.SetSpec(spec, actorId, now);
            filled++;
        }

        await db.SaveChangesAsync();
        return filled;
    }
}
