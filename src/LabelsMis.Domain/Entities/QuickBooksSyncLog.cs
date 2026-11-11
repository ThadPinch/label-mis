using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

/// <summary>One attempt at something involving QuickBooks: a connect, a token refresh, a read, and
/// later every invoice push. Append-only; a row is never edited. It holds ids, status and error
/// text, never a token and never the contents of a QuickBooks record.
/// <see cref="EntityBase.CreatedAt"/> is when it happened and <see cref="EntityBase.CreatedById"/>
/// is who pressed the button, or for <see cref="Automatic"/> rows the person whose connection the
/// background work ran under.</summary>
public class QuickBooksSyncLog : EntityBase
{
    public const int MaxMessageLength = 1000;
    public const int MaxOperationLength = 60;

    private QuickBooksSyncLog()
    {
    }

    /// <summary>One of <see cref="QuickBooks.QuickBooksOperations"/>.</summary>
    public string Operation { get; private set; } = string.Empty;
    public bool Succeeded { get; private set; }

    /// <summary>Done by a background task, not by someone at a screen.</summary>
    public bool Automatic { get; private set; }

    public Guid? InvoiceId { get; private set; }
    public Guid? CustomerId { get; private set; }

    /// <summary>QuickBooks' own id for the record the attempt touched or created.</summary>
    public string? QuickBooksId { get; private set; }
    public int? HttpStatus { get; private set; }

    /// <summary>QuickBooks' fault code (6140 = duplicate number) or OAuth's error word.</summary>
    public string? ErrorCode { get; private set; }
    public string? Message { get; private set; }

    /// <summary>Intuit's trace id; their support asks for it.</summary>
    public string? IntuitTid { get; private set; }
    public int? DurationMs { get; private set; }

    public static QuickBooksSyncLog Create(
        Guid id,
        string operation,
        bool succeeded,
        Guid actorId,
        bool automatic,
        DateTime occurredAt,
        Guid? invoiceId = null,
        Guid? customerId = null,
        string? quickBooksId = null,
        int? httpStatus = null,
        string? errorCode = null,
        string? message = null,
        string? intuitTid = null,
        int? durationMs = null)
    {
        operation = (operation ?? string.Empty).Trim();
        if (operation.Length == 0)
        {
            throw new ArgumentException("A log entry needs an operation.");
        }

        var entry = new QuickBooksSyncLog
        {
            Operation = Cut(operation, MaxOperationLength)!,
            Succeeded = succeeded,
            Automatic = automatic,
            InvoiceId = invoiceId,
            CustomerId = customerId,
            QuickBooksId = Cut(quickBooksId, 50),
            HttpStatus = httpStatus,
            ErrorCode = Cut(errorCode, 50),
            Message = Cut(message, MaxMessageLength),
            IntuitTid = Cut(intuitTid, 100),
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
