using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Background;

/// <summary>
/// Sends queued invoices to QuickBooks, one at a time. Every few seconds it makes one cheap check
/// (is QuickBooks connected, is anything due); while QuickBooks isn't connected it does nothing.
/// An invoice is claimed in a short transaction with <c>FOR UPDATE SKIP LOCKED</c>, oldest in line
/// first and not before its next-try time, so two copies of the app (during a deploy) never take
/// the same one. Each invoice gets its own scope and is sent as the person who queued it.
/// On start, sends cut off by a stop or crash go back in line; that is safe because every create
/// is preceded by a look-up of the invoice number.
/// </summary>
public class QuickBooksSendWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<QuickBooksSendWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await ReleaseStuckAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "QuickBooks send worker could not release interrupted sends");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain what is due, then rest.
                while (await RunOnceAsync(stoppingToken))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "QuickBooks send worker pass failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Puts every invoice left "Sending" (the app stopped mid-send) back in line.
    /// Returns how many.</summary>
    public async Task<int> ReleaseStuckAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
        var stuck = await db.Invoices.Where(i => i.QbExportStatus == QuickBooksExportStatus.Sending).ToListAsync(cancellationToken);
        foreach (var invoice in stuck)
        {
            invoice.ReleaseStuckQuickBooksAttempt();
        }

        await db.SaveChangesAsync(cancellationToken);
        if (stuck.Count > 0)
        {
            logger.LogInformation("Put {Count} interrupted QuickBooks send(s) back in line", stuck.Count);
        }

        return stuck.Count;
    }

    /// <summary>Claims and sends the next due invoice. Returns false when there was nothing to do
    /// (or QuickBooks isn't connected).</summary>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        var invoiceId = await ClaimNextAsync(cancellationToken);
        if (invoiceId is null)
        {
            return false;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<QuickBooksInvoiceSender>()
                .SendAsync(invoiceId.Value, cancellationToken);
            logger.LogInformation("QuickBooks send of invoice {InvoiceId}: {Outcome}", invoiceId, result.Outcome);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The sender settles every QuickBooks failure itself; this is something else (the
            // database, a bug). Put the invoice back in line rather than leave it "Sending".
            logger.LogError(ex, "QuickBooks send of invoice {InvoiceId} failed", invoiceId);
            await DeferAfterCrashAsync(invoiceId.Value);
        }

        return true;
    }

    private async Task<Guid?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
        var connected = await db.QuickBooksConnections.AsNoTracking()
            .AnyAsync(c => c.RealmIdProtected != null && c.RefreshTokenProtected != null && !c.NeedsReconnect, cancellationToken);
        if (!connected)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var anyDue = await db.Invoices.AsNoTracking()
            .AnyAsync(i => i.QbExportStatus == QuickBooksExportStatus.Queued && (i.QbNextAttemptAt == null || i.QbNextAttemptAt <= now),
                cancellationToken);
        if (!anyDue)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var queued = (int)QuickBooksExportStatus.Queued;
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM "Invoice"
            WHERE "QbExportStatus" = {queued} AND ("QbNextAttemptAt" IS NULL OR "QbNextAttemptAt" <= {now})
            ORDER BY "QbQueuedAt" NULLS LAST, "Id"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        var invoice = await db.Invoices.SingleAsync(i => i.Id == ids[0], cancellationToken);
        invoice.BeginQuickBooksAttempt(now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.Id;
    }

    private async Task DeferAfterCrashAsync(Guid invoiceId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
            var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId);
            if (invoice?.QbExportStatus == QuickBooksExportStatus.Sending)
            {
                invoice.DeferQuickBooksAttempt(
                    "LabelShop was interrupted while sending this invoice; it will try again.",
                    null,
                    time.GetUtcNow().UtcDateTime + QuickBooksInvoiceSender.RetryDelays[0]);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not put invoice {InvoiceId} back in line", invoiceId);
        }
    }
}
