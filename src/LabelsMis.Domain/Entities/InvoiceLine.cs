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
