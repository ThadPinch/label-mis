using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Invoice : EntityBase
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<Payment> _payments = [];

    private Invoice()
    {
    }

    public string InvoiceNumber { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;
    public Guid SalesOrderId { get; private set; }
    public SalesOrder SalesOrder { get; private set; } = null!;
    public Guid? ShipmentId { get; private set; }
    public Shipment? Shipment { get; private set; }
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal ShippingAmount { get; private set; }
    public decimal Total { get; private set; }
    public decimal BalanceDue { get; private set; }
    public DateTime? QbExportedAt { get; private set; }
    public string? QbInvoiceId { get; private set; }
    public string? Notes { get; private set; }
    public string? PdfFilePath { get; private set; }
    public string? VoidReason { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines;
    public IReadOnlyCollection<Payment> Payments => _payments;

    public static Invoice CreateDraft(
        Guid id,
        string invoiceNumber,
        Guid customerId,
        Guid salesOrderId,
        Guid? shipmentId,
        DateOnly invoiceDate,
        DateOnly dueDate,
        decimal subtotal,
        decimal taxAmount,
        decimal shippingAmount,
        string? notes,
        Guid createdById,
        DateTime createdAt)
    {
        var total = subtotal + taxAmount + shippingAmount;
        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = customerId,
            SalesOrderId = salesOrderId,
            ShipmentId = shipmentId,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            Status = InvoiceStatus.Draft,
            Subtotal = subtotal,
            TaxAmount = taxAmount,
            ShippingAmount = shippingAmount,
            Total = total,
            BalanceDue = total,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        invoice.SetCreated(id, createdById, createdAt);
        return invoice;
    }

    public void ReplaceLines(IEnumerable<InvoiceLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        _lines.AddRange(lines);
        RecalculateTotals();
    }

    public void MarkSent(string? pdfFilePath, Guid modifiedById, DateTime modifiedAt)
    {
        EnsureDraft();
        Status = InvoiceStatus.Sent;
        PdfFilePath = pdfFilePath;
        SetModified(modifiedById, modifiedAt);
    }

    public void RecordPayment(Payment payment)
    {
        if (Status is InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Cannot record payment on void invoice.");
        }

        if (payment.Amount > BalanceDue)
        {
            throw new InvalidOperationException("Payment exceeds balance due.");
        }

        _payments.Add(payment);
        BalanceDue -= payment.Amount;
        Status = BalanceDue <= 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }

    public void Void(string reason, Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is InvoiceStatus.Paid)
        {
            throw new InvalidOperationException("Paid invoices cannot be voided.");
        }

        Status = InvoiceStatus.Void;
        VoidReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        BalanceDue = 0;
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkExported(DateTime exportedAt, Guid modifiedById, DateTime modifiedAt)
    {
        QbExportedAt = exportedAt;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddLine(InvoiceLine line) => _lines.Add(line);
    public void AddPayment(Payment payment) => RecordPayment(payment);

    private void RecalculateTotals()
    {
        Subtotal = _lines.Sum(l => l.LineTotal);
        Total = Subtotal + TaxAmount + ShippingAmount;
        BalanceDue = Total - _payments.Sum(p => p.Amount);
    }

    private void EnsureDraft()
    {
        if (Status is not InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Only draft invoices can be edited.");
        }
    }
}
