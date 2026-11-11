using LabelsMis.Domain.Entities;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>What happened in one attempt, as the caller knows it.</summary>
public sealed record QuickBooksLogEntry(
    string Operation,
    bool Succeeded,
    Guid ActorId,
    bool Automatic = false,
    Guid? InvoiceId = null,
    Guid? CustomerId = null,
    string? QuickBooksId = null,
    int? HttpStatus = null,
    string? ErrorCode = null,
    string? Message = null,
    string? IntuitTid = null,
    int? DurationMs = null)
{
    public static QuickBooksLogEntry Ok(
        string operation, Guid actorId, bool automatic = false, QuickBooksApiResponse? response = null,
        string? message = null, string? quickBooksId = null, Guid? invoiceId = null, Guid? customerId = null) =>
        new(operation, true, actorId, automatic, invoiceId, customerId, quickBooksId,
            response?.StatusCode, null, message, response?.IntuitTid, response?.DurationMs);

    /// <summary>Takes status, fault code and trace id from the QuickBooks exception types and the
    /// plain message from anything else.</summary>
    public static QuickBooksLogEntry Failed(
        string operation, Guid actorId, Exception exception, bool automatic = false,
        Guid? invoiceId = null, Guid? customerId = null) => exception switch
    {
        QuickBooksApiException api => new(operation, false, actorId, automatic, invoiceId, customerId, null,
            api.StatusCode, api.Code ?? api.Kind.ToString(), api.Message, api.IntuitTid),
        QuickBooksAuthException auth => new(operation, false, actorId, automatic, invoiceId, customerId, null,
            auth.StatusCode, auth.Error, auth.Message, auth.IntuitTid),
        _ => new(operation, false, actorId, automatic, invoiceId, customerId, Message: exception.Message)
    };
}

public sealed record QuickBooksLogRow(
    DateTime OccurredAt,
    string Operation,
    bool Succeeded,
    bool Automatic,
    string? Who,
    Guid? InvoiceId,
    string? InvoiceNumber,
    string? QuickBooksId,
    int? HttpStatus,
    string? ErrorCode,
    string? Message,
    string? IntuitTid,
    int? DurationMs);

public sealed record QuickBooksLogPage(IReadOnlyList<QuickBooksLogRow> Rows, int Page, int PageCount, int Total, bool FailuresOnly);

/// <summary>Writes the QuickBooks log. Each entry goes through its own database scope, so it is
/// kept even when the work it describes rolls back, and it never flushes the caller's half-made
/// changes. A log write that fails is reported to the server log and otherwise ignored: the log
/// must not be the reason a push fails.</summary>
public class QuickBooksSyncLogger(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<QuickBooksSyncLogger> logger)
{
    public const int PageSize = 100;

    public async Task WriteAsync(QuickBooksLogEntry entry)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
            db.QuickBooksSyncLogs.Add(QuickBooksSyncLog.Create(
                Guid.NewGuid(),
                entry.Operation,
                entry.Succeeded,
                entry.ActorId,
                entry.Automatic,
                time.GetUtcNow().UtcDateTime,
                entry.InvoiceId,
                entry.CustomerId,
                entry.QuickBooksId,
                entry.HttpStatus,
                entry.ErrorCode,
                entry.Message,
                entry.IntuitTid,
                entry.DurationMs));

            // Not the request's token: the entry should land even if the browser went away.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the QuickBooks log entry for {Operation}", entry.Operation);
        }
    }

    public async Task<QuickBooksLogPage> GetPageAsync(int page, bool failuresOnly, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();

        var query = db.QuickBooksSyncLogs.AsNoTracking();
        if (failuresOnly)
        {
            query = query.Where(l => !l.Succeeded);
        }

        var total = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pageCount);

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        var userIds = logs.Select(l => l.CreatedById).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName, cancellationToken);

        var invoiceIds = logs.Where(l => l.InvoiceId is not null).Select(l => l.InvoiceId!.Value).Distinct().ToList();
        var invoices = invoiceIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Invoices.AsNoTracking()
                .Where(i => invoiceIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, cancellationToken);

        var rows = logs.Select(log => new QuickBooksLogRow(
            log.CreatedAt,
            log.Operation,
            log.Succeeded,
            log.Automatic,
            users.GetValueOrDefault(log.CreatedById),
            log.InvoiceId,
            log.InvoiceId is { } invoiceId ? invoices.GetValueOrDefault(invoiceId) : null,
            log.QuickBooksId,
            log.HttpStatus,
            log.ErrorCode,
            log.Message,
            log.IntuitTid,
            log.DurationMs)).ToList();

        return new QuickBooksLogPage(rows, page, pageCount, total, failuresOnly);
    }
}
