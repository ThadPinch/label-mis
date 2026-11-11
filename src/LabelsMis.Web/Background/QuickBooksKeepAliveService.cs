using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Background;

/// <summary>Keeps the QuickBooks sign-in alive. Intuit ends a refresh token that goes unused for
/// 100 days and changes its value about once a day, so the token is refreshed daily even when
/// nobody sends an invoice. With no company connected a pass is one single-row read and nothing
/// else: Intuit is never called.</summary>
public class QuickBooksKeepAliveService(
    IServiceScopeFactory scopeFactory,
    QuickBooksTokenService tokens,
    TimeProvider time,
    ILogger<QuickBooksKeepAliveService> logger) : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Already on the connection row and in the activity log; this line is for the server log.
                logger.LogWarning("QuickBooks keep-alive did not refresh the sign-in: {Reason}", ex.Message);
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Refreshes the sign-in when it is due. Returns whether a refresh was attempted.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        Guid actorId;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
            var connection = await db.QuickBooksConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (connection is null || !connection.KeepAliveDue(time.GetUtcNow().UtcDateTime))
            {
                return false;
            }

            // Log rows need a person: background work is filed under whoever connected.
            actorId = connection.ConnectedById ?? connection.CreatedById;
        }

        try
        {
            await tokens.RefreshAsync(actorId, automatic: true, cancellationToken: cancellationToken);
        }
        catch (QuickBooksNotConnectedException)
        {
            // The second refusal: the connection is now flagged and the invoice pages say so.
        }

        return true;
    }
}
