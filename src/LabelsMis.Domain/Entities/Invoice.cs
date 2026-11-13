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

    /// <summary>Ready for QuickBooks before the order ships (deposits, prepay, charge-only orders).</summary>
    public bool BillBeforeShip { get; private set; }

    /// <summary>Where the invoice is on its way to QuickBooks through the API.</summary>
    public QuickBooksExportStatus QbExportStatus { get; private set; }

    /// <summary>QuickBooks' version stamp for its copy; an update or a void has to quote it.</summary>
    public string? QbSyncToken { get; private set; }
    public DateTime? QbQueuedAt { get; private set; }

    /// <summary>Who asked for the send; the background worker's log rows are filed under them.</summary>
    public Guid? QbQueuedById { get; private set; }
    public DateTime? QbLastAttemptAt { get; private set; }

    /// <summary>Not before this time: set when a temporary failure is waiting to be retried.</summary>
    public DateTime? QbNextAttemptAt { get; private set; }
    public int QbAttemptCount { get; private set; }
    public string? QbLastError { get; private set; }
    public string? QbLastErrorCode { get; private set; }

    /// <summary>The total and the tax QuickBooks worked out, kept beside LabelShop's own.</summary>
    public decimal? QbTotal { get; private set; }
    public decimal? QbTax { get; private set; }

    /// <summary>A fingerprint of what was last sent, so a change after the export can be told apart
    /// from a repeat.</summary>
    public string? QbPayloadHash { get; private set; }
    public DateTime? QbVoidedAt { get; private set; }

    /// <summary>Create it in QuickBooks without emailing it, whatever the send setting says.</summary>
    public bool QbHoldSend { get; private set; }

    /// <summary>Email this invoice here instead of to the customer's billing contact.</summary>
    public string? QbSendToOverride { get; private set; }

    /// <summary>Exported through the API (or linked to a QuickBooks invoice found by its number), as
    /// opposed to marked exported by hand.</summary>
    public bool IsLinkedToQuickBooks => QbInvoiceId is not null && QbExportedAt is not null;

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

    public void SetBillBeforeShip(bool billBeforeShip, Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Void invoices cannot be changed.");
        }

        if (QbExportedAt is not null)
        {
            throw new InvalidOperationException("This invoice is already in QuickBooks.");
        }

        BillBeforeShip = billBeforeShip;
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

        if (QbExportStatus is QuickBooksExportStatus.Sending)
        {
            throw new InvalidOperationException("This invoice is being sent to QuickBooks right now; try again in a moment.");
        }

        QbExportedAt = exportedAt;
        QbExportedById = modifiedById;
        ClearQuickBooksAttempt();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Reopens the invoice. The QuickBooks id stays, so the next send updates the same
    /// QuickBooks invoice instead of making a second one.</summary>
    public void UnmarkExported(Guid modifiedById, DateTime modifiedAt)
    {
        if (QbExportStatus is QuickBooksExportStatus.Sending)
        {
            throw new InvalidOperationException("This invoice is being sent to QuickBooks right now; try again in a moment.");
        }

        QbExportedAt = null;
        QbExportedById = null;
        if (QbExportStatus is QuickBooksExportStatus.Queued)
        {
            ClearQuickBooksAttempt();
        }

        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Puts the invoice in line for the background worker. What it does there follows
    /// from the invoice: a void one is voided in QuickBooks, one with a QuickBooks id is updated,
    /// any other is created (after a look for its number). Whether the invoice is Ready is the
    /// caller's check; these are the rules that hold whatever the caller thinks.</summary>
    public void QueueForQuickBooks(Guid requestedById, DateTime now)
    {
        if (QbExportStatus is QuickBooksExportStatus.Queued or QuickBooksExportStatus.Sending)
        {
            throw new InvalidOperationException("This invoice is already on its way to QuickBooks.");
        }

        if (NonBillable)
        {
            throw new InvalidOperationException("Non-billable invoices are closed without a QuickBooks record.");
        }

        if (Status is InvoiceStatus.Void)
        {
            if (QbInvoiceId is null)
            {
                throw new InvalidOperationException("This void invoice never reached QuickBooks, so there is nothing to void there.");
            }

            if (QbVoidedAt is not null)
            {
                throw new InvalidOperationException("This invoice is already void in QuickBooks.");
            }
        }
        else if (QbExportedAt is not null)
        {
            throw new InvalidOperationException(
                "This invoice is already in QuickBooks. To send a change, unmark it exported, make the change, then send it again.");
        }

        QbExportStatus = QuickBooksExportStatus.Queued;
        QbQueuedAt = now;
        QbQueuedById = requestedById;
        QbAttemptCount = 0;
        QbNextAttemptAt = null;
        QbLastError = null;
        QbLastErrorCode = null;
        SetModified(requestedById, now);
    }

    /// <summary>The worker picks the invoice up.</summary>
    public void BeginQuickBooksAttempt(DateTime now)
    {
        if (QbExportStatus is not QuickBooksExportStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued invoice can be sent.");
        }

        QbExportStatus = QuickBooksExportStatus.Sending;
        QbLastAttemptAt = now;
        QbAttemptCount++;
    }

    /// <summary>QuickBooks has the invoice: created, updated, or found there under this number.
    /// Marks it exported the way the manual button does, and keeps QuickBooks' id, version stamp
    /// and figures.</summary>
    public void RecordInQuickBooks(
        string quickBooksInvoiceId,
        string? syncToken,
        decimal? quickBooksTotal,
        decimal? quickBooksTax,
        string? payloadHash,
        Guid actorId,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(quickBooksInvoiceId))
        {
            throw new ArgumentException("QuickBooks did not say which invoice this is.", nameof(quickBooksInvoiceId));
        }

        if (Status is InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Void invoices cannot be marked exported.");
        }

        if (NonBillable)
        {
            throw new InvalidOperationException("Non-billable invoices are closed without a QuickBooks record.");
        }

        QbInvoiceId = quickBooksInvoiceId.Trim();
        QbSyncToken = Clip(syncToken, 20);
        QbTotal = quickBooksTotal;
        QbTax = quickBooksTax;
        QbPayloadHash = payloadHash ?? QbPayloadHash;
        QbExportedAt ??= now;
        QbExportedById ??= QbQueuedById ?? actorId;
        ClearQuickBooksAttempt();
        SetModified(actorId, now);
    }

    /// <summary>The QuickBooks copy has been voided to match this void invoice.</summary>
    public void RecordVoidedInQuickBooks(string? syncToken, Guid actorId, DateTime now)
    {
        if (Status is not InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Only a void invoice is voided in QuickBooks.");
        }

        QbSyncToken = Clip(syncToken, 20) ?? QbSyncToken;
        QbVoidedAt = now;
        ClearQuickBooksAttempt();
        SetModified(actorId, now);
    }

    /// <summary>Stops the send until a person acts. <paramref name="message"/> is what they read.</summary>
    public void FlagQuickBooksAttention(string message, string? errorCode, DateTime now)
    {
        QbExportStatus = QuickBooksExportStatus.NeedsAttention;
        QbLastError = Clip(string.IsNullOrWhiteSpace(message) ? "QuickBooks did not accept this invoice." : message.Trim(), 1000);
        QbLastErrorCode = Clip(errorCode, 50);
        QbNextAttemptAt = null;
        QbLastAttemptAt ??= now;
    }

    /// <summary>A temporary failure: back in line, not before <paramref name="nextAttemptAt"/>.</summary>
    public void DeferQuickBooksAttempt(string message, string? errorCode, DateTime nextAttemptAt)
    {
        QbExportStatus = QuickBooksExportStatus.Queued;
        QbLastError = Clip(message?.Trim(), 1000);
        QbLastErrorCode = Clip(errorCode, 50);
        QbNextAttemptAt = nextAttemptAt;
    }

    /// <summary>A send the app was stopped in the middle of goes back in line. Safe, because every
    /// attempt first looks for the invoice in QuickBooks.</summary>
    public void ReleaseStuckQuickBooksAttempt()
    {
        if (QbExportStatus is QuickBooksExportStatus.Sending)
        {
            QbExportStatus = QuickBooksExportStatus.Queued;
            QbNextAttemptAt = null;
        }
    }

    /// <summary>Takes the invoice out of line, or clears a "needs attention" that was settled by hand.</summary>
    public void CancelQuickBooksSend(Guid modifiedById, DateTime now)
    {
        if (QbExportStatus is QuickBooksExportStatus.Sending)
        {
            throw new InvalidOperationException("This invoice is being sent to QuickBooks right now; try again in a moment.");
        }

        ClearQuickBooksAttempt();
        SetModified(modifiedById, now);
    }

    /// <summary>Per-invoice sending: hold the email, or send it to another address once.</summary>
    public void SetQuickBooksSendOptions(bool holdSend, string? sendToOverride, Guid modifiedById, DateTime now)
    {
        var address = string.IsNullOrWhiteSpace(sendToOverride) ? null : sendToOverride.Trim();
        if (address is not null && (address.Length > 320 || !address.Contains('@') || address.Contains(' ')))
        {
            throw new ArgumentException("That doesn't look like an email address.", nameof(sendToOverride));
        }

        QbHoldSend = holdSend;
        QbSendToOverride = address;
        SetModified(modifiedById, now);
    }

    private void ClearQuickBooksAttempt()
    {
        QbExportStatus = QuickBooksExportStatus.None;
        QbNextAttemptAt = null;
        QbLastError = null;
        QbLastErrorCode = null;
    }

    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length > max ? value[..max] : value;

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
