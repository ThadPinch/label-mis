using LabelsMis.Domain.Carriers;
using LabelsMis.Web.Services.Carriers;

namespace LabelsMis.Web.Background;

/// <summary>What one pass of the tracking check did.</summary>
public sealed record TrackingRunResult(int Due, int Asked, int Changed, int Delivered, string? StoppedBecause);

/// <summary>Asks the carrier, every half hour, where the labels bought through LabelShop are, and
/// turns a shipment Delivered once every box is. Gentle by design: only real labels (production
/// keys) that are still in transit, each at most once every few hours, a handful per pass, one
/// call a second, and the pass ends at the first sign the carrier is down. With no keys saved, or
/// tracking switched off in Setup → Shipping carrier, a pass is two small reads and nothing else.
/// Each shipment gets its own service scope, so nothing piles up over the life of the process.</summary>
public class ShipmentTrackingPoller(
    IServiceScopeFactory scopeFactory,
    ILogger<ShipmentTrackingPoller> logger) : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    /// <summary>The most shipments one pass asks about.</summary>
    public const int MaxPerPass = 25;

    /// <summary>The wait between two questions to the carrier.</summary>
    public TimeSpan Pace { get; init; } = TimeSpan.FromSeconds(1);

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
                var run = await RunOnceAsync(stoppingToken);
                if (run.Asked > 0 || run.StoppedBecause is not null)
                {
                    logger.LogInformation(
                        "Tracking check: {Due} due, {Asked} asked, {Changed} changed, {Delivered} delivered{Stopped}",
                        run.Due, run.Asked, run.Changed, run.Delivered,
                        run.StoppedBecause is null ? string.Empty : $"; stopped: {run.StoppedBecause}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Tracking check failed");
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

    /// <summary>One pass: asks about the shipments that are due, oldest check first.</summary>
    public async Task<TrackingRunResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        List<Guid> due;
        using (var scope = scopeFactory.CreateScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<CarrierShipmentService>()
                .GetTrackingDueAsync(MaxPerPass, cancellationToken);
        }

        var asked = 0;
        var changed = 0;
        var delivered = 0;
        string? stoppedBecause = null;
        foreach (var shipmentId in due)
        {
            if (asked > 0 && Pace > TimeSpan.Zero)
            {
                await Task.Delay(Pace, cancellationToken);
            }

            using var scope = scopeFactory.CreateScope();
            var labels = scope.ServiceProvider.GetRequiredService<CarrierShipmentService>();
            try
            {
                var result = await labels.RefreshTrackingAsync(shipmentId, CarrierCaller.Background(shipmentId), cancellationToken);
                asked++;
                if (result.Changed) changed++;
                if (result.Delivered) delivered++;
            }
            catch (CarrierException ex) when (ex.Kind is CarrierErrorKind.Refused)
            {
                // The carrier answered and said no about this one: move on, and leave it alone for a while.
                asked++;
                await labels.NoteTrackingAskedAsync(shipmentId, cancellationToken);
            }
            catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException or InvalidOperationException)
            {
                // The carrier is not answering, or the keys are not good: asking about the rest
                // would only repeat the failure. The next pass starts again from the same shipment.
                stoppedBecause = ex.Message;
                break;
            }
        }

        return new TrackingRunResult(due.Count, asked, changed, delivered, stoppedBecause);
    }
}
