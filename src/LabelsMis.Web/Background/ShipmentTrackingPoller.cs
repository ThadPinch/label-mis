using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Fedex;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Background;

public class ShipmentTrackingPoller(
    IServiceProvider serviceProvider,
    ILogger<ShipmentTrackingPoller> logger) : BackgroundService
{
    private static readonly TimeSpan NormalInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = NormalInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
                delay = NormalInterval;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Shipment tracking poll failed; backing off for {Delay}", delay);
                delay = TimeSpan.FromMinutes(Math.Min(delay.TotalMinutes * 2, MaxBackoff.TotalMinutes));
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
        var fedex = scope.ServiceProvider.GetRequiredService<IFedexClient>();

        var shipments = await db.Shipments
            .Include(s => s.Packages).ThenInclude(p => p.TrackingEvents)
            .Where(s => s.Status == ShipmentStatus.InTransit || s.Status == ShipmentStatus.Pending)
            .ToListAsync(cancellationToken);

        if (shipments.Count == 0)
        {
            return;
        }

        var systemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var now = DateTime.UtcNow;
        var updated = false;

        foreach (var shipment in shipments)
        {
            foreach (var package in shipment.Packages.Where(p => !string.IsNullOrWhiteSpace(p.TrackingNumber)))
            {
                var events = await fedex.GetTrackingAsync(package.TrackingNumber!, cancellationToken);
                var existingDescriptions = package.TrackingEvents
                    .Select(e => $"{e.EventAt:O}|{e.StatusDescription}")
                    .ToHashSet();

                foreach (var trackingEvent in events)
                {
                    var key = $"{trackingEvent.EventAt:O}|{trackingEvent.StatusDescription}";
                    if (existingDescriptions.Contains(key))
                    {
                        continue;
                    }

                    var entity = TrackingEvent.Create(
                        Guid.NewGuid(),
                        package.Id,
                        trackingEvent.EventAt,
                        trackingEvent.StatusDescription,
                        trackingEvent.Location,
                        null,
                        systemUserId,
                        now);
                    package.AddTrackingEvent(entity);
                    db.TrackingEvents.Add(entity);
                    updated = true;

                    if (trackingEvent.StatusDescription.Contains("Delivered", StringComparison.OrdinalIgnoreCase))
                    {
                        shipment.MarkDelivered(systemUserId, now);
                    }
                }
            }
        }

        if (updated)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Updated tracking for {Count} in-transit shipments", shipments.Count);
        }
    }
}
