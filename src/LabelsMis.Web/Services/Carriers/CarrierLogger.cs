using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Carriers;

/// <summary>What happened in one call to the carrier, as the caller knows it.</summary>
public sealed record CarrierLogEntry(
    string Operation,
    bool Succeeded,
    Guid ActorId,
    CarrierEnvironment Environment,
    bool Automatic = false,
    Guid? SalesOrderId = null,
    Guid? ShipmentId = null,
    string? Detail = null,
    int? HttpStatus = null,
    string? ErrorKind = null,
    string? Message = null,
    string? CorrelationId = null,
    int? DurationMs = null);

public sealed record CarrierLogRow(
    DateTime OccurredAt,
    string Operation,
    bool Succeeded,
    bool Automatic,
    CarrierEnvironment Environment,
    string? Who,
    Guid? ShipmentId,
    string? ShipmentNumber,
    string? Detail,
    int? HttpStatus,
    string? Message,
    string? CorrelationId,
    int? DurationMs);

/// <summary>Writes the carrier log. Each entry goes through its own database scope, so it is kept
/// even when the work it describes rolls back, and it never flushes the caller's half-made
/// changes. A log write that fails is reported to the server log and otherwise ignored: the log
/// must not be the reason a label isn't bought.</summary>
public class CarrierLogger(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<CarrierLogger> logger)
{
    public async Task WriteAsync(CarrierLogEntry entry)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
            db.CarrierLogs.Add(CarrierLog.Create(
                Guid.NewGuid(),
                entry.Operation,
                entry.Succeeded,
                entry.ActorId,
                entry.Automatic,
                entry.Environment,
                time.GetUtcNow().UtcDateTime,
                entry.SalesOrderId,
                entry.ShipmentId,
                entry.Detail,
                entry.HttpStatus,
                entry.ErrorKind,
                entry.Message,
                entry.CorrelationId,
                entry.DurationMs));

            // Not the request's token: the entry should land even if the browser went away.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the carrier log entry for {Operation}", entry.Operation);
        }
    }

    /// <summary>The newest entries, for the settings page (or one shipment's own history).</summary>
    public async Task<IReadOnlyList<CarrierLogRow>> GetRecentAsync(
        int count, Guid? shipmentId = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();

        var query = db.CarrierLogs.AsNoTracking();
        if (shipmentId is { } id)
        {
            query = query.Where(l => l.ShipmentId == id);
        }

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .Take(Math.Clamp(count, 1, 500))
            .ToListAsync(cancellationToken);

        var userIds = logs.Select(l => l.CreatedById).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName, cancellationToken);

        var shipmentIds = logs.Where(l => l.ShipmentId is not null).Select(l => l.ShipmentId!.Value).Distinct().ToList();
        var shipments = shipmentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Shipments.AsNoTracking()
                .Where(s => shipmentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.ShipmentNumber, cancellationToken);

        return logs.Select(l => new CarrierLogRow(
            l.CreatedAt,
            l.Operation,
            l.Succeeded,
            l.Automatic,
            l.Environment,
            l.Automatic ? null : users.GetValueOrDefault(l.CreatedById),
            l.ShipmentId,
            l.ShipmentId is { } shipment ? shipments.GetValueOrDefault(shipment) : null,
            l.Detail,
            l.HttpStatus,
            l.Message,
            l.CorrelationId,
            l.DurationMs)).ToList();
    }
}
