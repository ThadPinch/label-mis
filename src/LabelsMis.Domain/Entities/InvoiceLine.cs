using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class InvoiceLine : EntityBase
{
    private InvoiceLine()
    {
    }

    public Guid InvoiceId { get; private set; }
    public Invoice Invoice { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public Guid? SalesOrderLineId { get; private set; }
    public Guid? JobId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public string? TaxCode { get; private set; }

    /// <summary>QuickBooks item chosen by hand on the preview; null = the Setup → QuickBooks mapping.</summary>
    public string? QbItemOverride { get; private set; }

    /// <summary>QuickBooks class chosen by hand on the preview; null = the mapping's class rule.</summary>
    public string? QbClassOverride { get; private set; }

    /// <summary>Sets (or, with blank, clears) the line's QuickBooks item and class overrides.</summary>
    public void SetQbOverrides(string? item, string? qbClass, Guid modifiedById, DateTime modifiedAt)
    {
        var trimmedItem = string.IsNullOrWhiteSpace(item) ? null : item.Trim();
        var trimmedClass = string.IsNullOrWhiteSpace(qbClass) ? null : qbClass.Trim();
        if (trimmedItem is { Length: > 100 } || trimmedClass is { Length: > 100 })
        {
            throw new ArgumentException("QuickBooks item and class names are at most 100 characters.");
        }

        QbItemOverride = trimmedItem;
        QbClassOverride = trimmedClass;
        SetModified(modifiedById, modifiedAt);
    }

    public static InvoiceLine Create(
        Guid id,
        Guid invoiceId,
        int lineNumber,
        Guid? salesOrderLineId,
        Guid? jobId,
        string description,
        int quantity,
        decimal unitPrice,
        string? taxCode,
        Guid createdById,
        DateTime createdAt)
    {
        var line = new InvoiceLine
        {
            InvoiceId = invoiceId,
            LineNumber = lineNumber,
            SalesOrderLineId = salesOrderLineId,
            JobId = jobId,
            Description = description.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity,
            TaxCode = taxCode
        };
        line.SetCreated(id, createdById, createdAt);
        return line;
    }
}
