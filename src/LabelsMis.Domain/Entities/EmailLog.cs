using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

/// <summary>Which business document an outbound email was about.</summary>
public enum EmailDocumentType
{
    Estimate = 1,
    Invoice = 2,
    PurchaseOrder = 3
}

/// <summary>One outbound email attempt, kept so users can see the send history of an estimate or
/// order without digging through Mailgun. Failed attempts are logged too, with the error.</summary>
public class EmailLog : EntityBase
{
    private EmailLog()
    {
    }

    public EmailDocumentType DocumentType { get; private set; }
    public Guid DocumentId { get; private set; }

    /// <summary>The sales order the document belongs to, when there is one (invoices), so an
    /// order page can show its invoice emails alongside its own.</summary>
    public Guid? SalesOrderId { get; private set; }

    public string To { get; private set; } = string.Empty;
    public string? Cc { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public bool HadAttachment { get; private set; }
    public DateTime SentAt { get; private set; }
    public bool Succeeded { get; private set; }
    public string? Error { get; private set; }

    public static EmailLog Create(
        Guid id,
        EmailDocumentType documentType,
        Guid documentId,
        Guid? salesOrderId,
        string to,
        string? cc,
        string subject,
        bool hadAttachment,
        bool succeeded,
        string? error,
        Guid sentById,
        DateTime sentAt)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            throw new ArgumentException("Recipient is required.", nameof(to));
        }

        var log = new EmailLog
        {
            DocumentType = documentType,
            DocumentId = documentId,
            SalesOrderId = salesOrderId,
            To = to.Trim(),
            Cc = string.IsNullOrWhiteSpace(cc) ? null : cc.Trim(),
            Subject = subject.Trim(),
            HadAttachment = hadAttachment,
            SentAt = sentAt,
            Succeeded = succeeded,
            Error = string.IsNullOrWhiteSpace(error) ? null : Truncate(error.Trim(), 1000)
        };
        log.SetCreated(id, sentById, sentAt);
        return log;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
