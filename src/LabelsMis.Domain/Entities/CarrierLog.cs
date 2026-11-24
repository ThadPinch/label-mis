using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

/// <summary>One call to the carrier: a sign-in, a rate, a booking, a label, a void, a tracking
/// check. Append-only; a row is never edited. It holds what was asked for in a few words, the
/// outcome and the carrier's own id for the call, never a key or a token and never an address.
/// <see cref="EntityBase.CreatedAt"/> is when it happened and <see cref="EntityBase.CreatedById"/>
/// is who pressed the button (the system user for <see cref="Automatic"/> rows).</summary>
public class CarrierLog : EntityBase
{
    public const int MaxOperationLength = 40;
    public const int MaxDetailLength = 300;
    public const int MaxMessageLength = 1000;

    private CarrierLog()
    {
    }

    /// <summary>One of <see cref="Carriers.CarrierOperations"/>.</summary>
    public string Operation { get; private set; } = string.Empty;
    public bool Succeeded { get; private set; }

    /// <summary>Done by a background task, not by someone at a screen.</summary>
    public bool Automatic { get; private set; }

    public CarrierEnvironment Environment { get; private set; }
    public Guid? SalesOrderId { get; private set; }
    public Guid? ShipmentId { get; private set; }

    /// <summary>What the call was about, in a few words: "UPS Ground $18.99, 2 boxes".</summary>
    public string? Detail { get; private set; }
    public int? HttpStatus { get; private set; }

    /// <summary>The <see cref="Carriers.CarrierErrorKind"/> of a failure.</summary>
    public string? ErrorKind { get; private set; }
    public string? Message { get; private set; }

    /// <summary>The carrier's id for the call; its support asks for it.</summary>
    public string? CorrelationId { get; private set; }
    public int? DurationMs { get; private set; }

    public static CarrierLog Create(
        Guid id,
        string operation,
        bool succeeded,
        Guid actorId,
        bool automatic,
        CarrierEnvironment environment,
        DateTime occurredAt,
        Guid? salesOrderId = null,
        Guid? shipmentId = null,
        string? detail = null,
        int? httpStatus = null,
        string? errorKind = null,
        string? message = null,
        string? correlationId = null,
        int? durationMs = null)
    {
        operation = (operation ?? string.Empty).Trim();
        if (operation.Length == 0)
        {
            throw new ArgumentException("A log entry needs an operation.");
        }

        var entry = new CarrierLog
        {
            Operation = Cut(operation, MaxOperationLength)!,
            Succeeded = succeeded,
            Automatic = automatic,
            Environment = environment,
            SalesOrderId = salesOrderId,
            ShipmentId = shipmentId,
            Detail = Cut(detail, MaxDetailLength),
            HttpStatus = httpStatus,
            ErrorKind = Cut(errorKind, 30),
            Message = Cut(message, MaxMessageLength),
            CorrelationId = Cut(correlationId, 100),
            DurationMs = durationMs
        };
        entry.SetCreated(id, actorId, occurredAt);
        return entry;
    }

    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
