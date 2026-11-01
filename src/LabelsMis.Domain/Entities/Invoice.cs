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

    /// <summary>Shipping set by hand on a draft invoice; when set it wins over the quoted and the
    /// shipment-derived amounts. Null = automatic.</summary>
    public decimal? ShippingOverride { get; private set; }
    public decimal Total { get; private set; }
    public decimal BalanceDue { get; private set; }
    public DateTime? QbExportedAt { get; private set; }
    public Guid? QbExportedById { get; private set; }
    public string? QbInvoiceId { get; private set; }
    public string? Notes { get; private set; }
    public string? PdfFilePath { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>Closed without a QuickBooks record (e.g. a $0 sample or an internal reprint): off
    /// the Ready list, never exported. Reversible until something is paid or exported.</summary>
    public bool NonBillable { get; private set; }
    public string? NonBillableReason { get; private set; }
    public DateTime? NonBillableAt { get; private set; }
    public Guid? NonBillableById { get; private set; }

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

    public void ReplaceLines(
        IEnumerable<InvoiceLine> lines,
        decimal taxAmount,
        decimal shippingAmount,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        EnsureDraft();
        _lines.Clear();
        _lines.AddRange(lines);
        TaxAmount = taxAmount;
        ShippingAmount = shippingAmount;
        RecalculateTotals();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Sets (or with null clears) the manual shipping override. The caller re-applies the
    /// resolved shipping amount afterwards.</summary>
    public void SetShippingOverride(decimal? shippingOverride, Guid modifiedById, DateTime modifiedAt)
    {
        EnsureEditable();
        if (shippingOverride < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(shippingOverride), "Shipping cannot be negative.");
        }

        ShippingOverride = shippingOverride;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Replaces the shipping amount on a draft, unexported invoice, moving the total and
    /// balance due by the difference (lines and payments are untouched).</summary>
    public void ApplyShipping(decimal shippingAmount, Guid modifiedById, DateTime modifiedAt)
    {
        EnsureEditable();
        var delta = shippingAmount - ShippingAmount;
        if (delta == 0m)
        {
            return;
        }

        ShippingAmount = shippingAmount;
        Total += delta;
        BalanceDue += delta;
        SetModified(modifiedById, modifiedAt);
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

    public void MarkNonBillable(string reason, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Say why the invoice is non-billable.", nameof(reason));
        }

        if (Status is InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Void invoices cannot be marked non-billable.");
        }

        if (QbExportedAt is not null)
        {
            throw new InvalidOperationException("This invoice is already in QuickBooks; unmark it exported first.");
        }

        if (Status is InvoiceStatus.Paid or InvoiceStatus.PartiallyPaid || BalanceDue != Total)
        {
            throw new InvalidOperationException("An invoice with payments cannot be non-billable.");
        }

        var trimmed = reason.Trim();
        NonBillable = true;
        NonBillableReason = trimmed.Length > 500 ? trimmed[..500] : trimmed;
        NonBillableAt = modifiedAt;
        NonBillableById = modifiedById;
        SetModified(modifiedById, modifiedAt);
    }

    public void ClearNonBillable(Guid modifiedById, DateTime modifiedAt)
    {
        NonBillable = false;
        NonBillableReason = null;
        NonBillableAt = null;
        NonBillableById = null;
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkExported(DateTime exportedAt, Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Void invoices cannot be marked exported.");
        }

        if (NonBillable)
        {
            throw new InvalidOperationException("Non-billable invoices are closed without a QuickBooks record.");
        }

        QbExportedAt = exportedAt;
        QbExportedById = modifiedById;
        SetModified(modifiedById, modifiedAt);
    }

    public void UnmarkExported(Guid modifiedById, DateTime modifiedAt)
    {
        QbExportedAt = null;
        QbExportedById = null;
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

    /// <summary>Sent and QuickBooks-exported invoices are never rewritten.</summary>
    private void EnsureEditable()
    {
        EnsureDraft();
        if (QbExportedAt is not null)
        {
            throw new InvalidOperationException("Exported invoices cannot be edited.");
        }
    }
}
