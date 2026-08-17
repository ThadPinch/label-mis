using LabelsMis.Domain.Common;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

/// <summary>
/// An item on a sales order that an outside vendor makes for us — either a whole order line (which
/// still gets a job, routed straight to ready-to-ship on receipt) or an additional charge (promo,
/// print, wide format; no job). Holds the vendor side of the deal (who, quote #, cost, expected date,
/// private notes) plus the tracking state: sent to vendor, receipts, complete.
/// Exactly one of <see cref="SalesOrderLineId"/> / <see cref="SalesOrderChargeId"/> is set.
/// </summary>
public class OutsourcedItem : EntityBase
{
    private readonly List<OutsourceReceipt> _receipts = [];

    private OutsourcedItem()
    {
    }

    public Guid SalesOrderId { get; private set; }
    public SalesOrder SalesOrder { get; private set; } = null!;
    public Guid? SalesOrderLineId { get; private set; }
    public SalesOrderLine? SalesOrderLine { get; private set; }
    public Guid? SalesOrderChargeId { get; private set; }
    public SalesOrderCharge? SalesOrderCharge { get; private set; }

    public Guid? VendorId { get; private set; }
    public Supplier? Vendor { get; private set; }
    public string? QuoteNumber { get; private set; }
    /// <summary>What the vendor charges us for the whole item (all units).</summary>
    public decimal? VendorCost { get; private set; }
    public DateOnly? ExpectedIn { get; private set; }
    /// <summary>Internal only — never printed on customer documents.</summary>
    public string? PrivateNotes { get; private set; }

    /// <summary>When the order/PO was sent to the vendor; null until marked sent.</summary>
    public DateTime? SentToVendorAt { get; private set; }
    /// <summary>When the item was received in full (or marked complete); null while still open.</summary>
    public DateTime? ReceivedAt { get; private set; }

    public IReadOnlyCollection<OutsourceReceipt> Receipts => _receipts;

    public bool IsLine => SalesOrderLineId.HasValue;
    public bool IsSent => SentToVendorAt.HasValue;
    public bool IsComplete => ReceivedAt.HasValue;
    public int QuantityReceived => _receipts.Sum(r => r.Quantity);

    public OutsourceDetails Details => new(VendorId, QuoteNumber, ExpectedIn, PrivateNotes);

    public static OutsourcedItem CreateForLine(
        Guid id,
        Guid salesOrderId,
        Guid salesOrderLineId,
        OutsourceDetails details,
        decimal? vendorCost,
        Guid createdById,
        DateTime createdAt) =>
        Create(id, salesOrderId, salesOrderLineId, null, details, vendorCost, createdById, createdAt);

    public static OutsourcedItem CreateForCharge(
        Guid id,
        Guid salesOrderId,
        Guid salesOrderChargeId,
        OutsourceDetails details,
        decimal? vendorCost,
        Guid createdById,
        DateTime createdAt) =>
        Create(id, salesOrderId, null, salesOrderChargeId, details, vendorCost, createdById, createdAt);

    private static OutsourcedItem Create(
        Guid id,
        Guid salesOrderId,
        Guid? salesOrderLineId,
        Guid? salesOrderChargeId,
        OutsourceDetails details,
        decimal? vendorCost,
        Guid createdById,
        DateTime createdAt)
    {
        ValidateCost(vendorCost);
        var normalized = details.Normalize();
        var item = new OutsourcedItem
        {
            SalesOrderId = salesOrderId,
            SalesOrderLineId = salesOrderLineId,
            SalesOrderChargeId = salesOrderChargeId,
            VendorId = normalized.VendorId,
            QuoteNumber = normalized.QuoteNumber,
            VendorCost = vendorCost,
            ExpectedIn = normalized.ExpectedIn,
            PrivateNotes = normalized.PrivateNotes
        };
        item.SetCreated(id, createdById, createdAt);
        return item;
    }

    /// <summary>Vendor, quote, cost, expected date and notes are editable for the life of the item
    /// (a vendor slips a date, a quote gets revised) — tracking state is untouched.</summary>
    public void UpdateDetails(OutsourceDetails details, decimal? vendorCost, Guid modifiedById, DateTime modifiedAt)
    {
        ValidateCost(vendorCost);
        var normalized = details.Normalize();
        VendorId = normalized.VendorId;
        QuoteNumber = normalized.QuoteNumber;
        VendorCost = vendorCost;
        ExpectedIn = normalized.ExpectedIn;
        PrivateNotes = normalized.PrivateNotes;
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkSent(DateTime sentAt, Guid modifiedById, DateTime modifiedAt)
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("This item has already been received.");
        }

        SentToVendorAt = sentAt;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>
    /// Records a delivery. The item completes when <paramref name="markComplete"/> is set or the
    /// cumulative quantity reaches <paramref name="quantityOrdered"/> — short-shipped items can be
    /// closed out explicitly, and a vendor's over-ship is simply recorded.
    /// </summary>
    public OutsourceReceipt Receive(
        Guid receiptId,
        DateOnly receivedOn,
        int quantity,
        string? notes,
        bool markComplete,
        int quantityOrdered,
        Guid userId,
        DateTime now)
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("This item has already been received in full.");
        }

        var receipt = OutsourceReceipt.Create(receiptId, Id, receivedOn, quantity, notes, userId, now);
        _receipts.Add(receipt);

        if (markComplete || QuantityReceived >= quantityOrdered)
        {
            ReceivedAt = now;
        }

        SetModified(userId, now);
        return receipt;
    }

    /// <summary>Closes out an item without a further delivery (e.g. the balance was cancelled).</summary>
    public void MarkComplete(Guid modifiedById, DateTime modifiedAt)
    {
        if (IsComplete)
        {
            return;
        }

        ReceivedAt = modifiedAt;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Whether outsourcing can still be switched off for the underlying line/charge — only
    /// before anything has happened with the vendor.</summary>
    public bool CanBeRemoved => !IsSent && _receipts.Count == 0 && !IsComplete;

    private static void ValidateCost(decimal? vendorCost)
    {
        if (vendorCost is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vendorCost), "Vendor cost cannot be negative.");
        }
    }
}
