using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

public class SalesOrder : EntityBase
{
    private readonly List<SalesOrderLine> _lines = [];
    private readonly List<SalesOrderCharge> _charges = [];

    private SalesOrder()
    {
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;
    public Guid? SourceEstimateId { get; private set; }
    public Estimate? SourceEstimate { get; private set; }
    public Guid? SalesRepId { get; private set; }
    public string? CustomerPoNumber { get; private set; }
    public DateTime OrderedAt { get; private set; }
    public DateOnly? RequestedShipDate { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Internal billing instructions carried over from the estimate. Never printed on a customer document.</summary>
    public string? BillingNotes { get; private set; }

    public Guid? ShippingMethodId { get; private set; }
    public ShippingMethod? ShippingMethod { get; private set; }
    public decimal ShippingCost { get; private set; }
    public string? ShipToName { get; private set; }
    public string? ShipToStreet1 { get; private set; }
    public string? ShipToStreet2 { get; private set; }
    public string? ShipToCity { get; private set; }
    public string? ShipToState { get; private set; }
    public string? ShipToZip { get; private set; }
    public string? ShipToCountry { get; private set; }

    public ShippingAddress ShippingAddress => new(
        ShipToName, ShipToStreet1, ShipToStreet2, ShipToCity, ShipToState, ShipToZip, ShipToCountry);

    public IReadOnlyCollection<SalesOrderLine> Lines => _lines;
    public IReadOnlyCollection<SalesOrderCharge> Charges => _charges;

    public static SalesOrder CreateOpen(
        Guid id,
        string orderNumber,
        Guid customerId,
        Guid? sourceEstimateId,
        Guid? salesRepId,
        string? customerPoNumber,
        DateTime orderedAt,
        DateOnly? requestedShipDate,
        string? notes,
        string? billingNotes,
        Guid? shippingMethodId,
        decimal shippingCost,
        ShippingAddress shippingAddress,
        Guid createdById,
        DateTime createdAt)
    {
        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            SourceEstimateId = sourceEstimateId,
            SalesRepId = salesRepId,
            CustomerPoNumber = string.IsNullOrWhiteSpace(customerPoNumber) ? null : customerPoNumber.Trim(),
            OrderedAt = orderedAt,
            RequestedShipDate = requestedShipDate,
            Status = SalesOrderStatus.Open,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            BillingNotes = string.IsNullOrWhiteSpace(billingNotes) ? null : billingNotes.Trim()
        };
        order.SetShipping(shippingMethodId, shippingCost, shippingAddress);
        order.SetCreated(id, createdById, createdAt);
        return order;
    }

    public void UpdateOpen(
        string? customerPoNumber,
        DateOnly? requestedShipDate,
        string? notes,
        string? billingNotes,
        Guid? shippingMethodId,
        decimal shippingCost,
        ShippingAddress shippingAddress,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        EnsureOpen();

        CustomerPoNumber = string.IsNullOrWhiteSpace(customerPoNumber) ? null : customerPoNumber.Trim();
        RequestedShipDate = requestedShipDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        BillingNotes = string.IsNullOrWhiteSpace(billingNotes) ? null : billingNotes.Trim();
        SetShipping(shippingMethodId, shippingCost, shippingAddress);
        SetModified(modifiedById, modifiedAt);
    }

    private void SetShipping(Guid? shippingMethodId, decimal shippingCost, ShippingAddress shippingAddress)
    {
        var address = (shippingAddress ?? ShippingAddress.Empty).Normalized();
        ShippingMethodId = shippingMethodId;
        ShippingCost = shippingCost < 0 ? 0 : shippingCost;
        ShipToName = address.RecipientName;
        ShipToStreet1 = address.Street1;
        ShipToStreet2 = address.Street2;
        ShipToCity = address.City;
        ShipToState = address.State;
        ShipToZip = address.Zip;
        ShipToCountry = address.Country;
    }

    /// <summary>
    /// Updates the order's header details regardless of status — the explicit "unlock" edit path
    /// for orders already in production. The customer and lines are handled separately.
    /// </summary>
    public void UpdateDetails(
        string? customerPoNumber,
        DateOnly? requestedShipDate,
        string? notes,
        string? billingNotes,
        Guid? shippingMethodId,
        decimal shippingCost,
        ShippingAddress shippingAddress,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        CustomerPoNumber = string.IsNullOrWhiteSpace(customerPoNumber) ? null : customerPoNumber.Trim();
        RequestedShipDate = requestedShipDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        BillingNotes = string.IsNullOrWhiteSpace(billingNotes) ? null : billingNotes.Trim();
        SetShipping(shippingMethodId, shippingCost, shippingAddress);
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Updates the order's header notes. Allowed in any status (e.g. edited from a job in production).</summary>
    public void UpdateNotes(string? notes, Guid modifiedById, DateTime modifiedAt)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void ReplaceLines(IEnumerable<SalesOrderLine> lines)
    {
        EnsureOpen();
        _lines.Clear();
        _lines.AddRange(lines);
    }

    public void AdvanceStatus(SalesOrderStatus status, Guid modifiedById, DateTime modifiedAt)
    {
        if (status <= Status)
        {
            throw new InvalidOperationException("Sales order status can only move forward.");
        }

        Status = status;
        SetModified(modifiedById, modifiedAt);
    }

    public void EnsureOpen()
    {
        if (Status is not SalesOrderStatus.Open)
        {
            throw new InvalidOperationException("Sales orders cannot be edited once production has started.");
        }
    }

    public void EnsureCanDelete()
    {
        if (Status is not SalesOrderStatus.Open)
        {
            throw new InvalidOperationException("Only open sales orders can be deleted.");
        }
    }

    public void Cancel(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is SalesOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Order is already cancelled.");
        }

        Status = SalesOrderStatus.Cancelled;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddLine(SalesOrderLine line) => _lines.Add(line);

    /// <summary>Replaces the order's non-label charges. Unlike lines, charges have no dependent
    /// jobs, so they may be replaced in any editable status (open or unlocked in-production edits).</summary>
    public void ReplaceCharges(IEnumerable<SalesOrderCharge> charges)
    {
        _charges.Clear();
        _charges.AddRange(charges);
    }

    public void AddCharge(SalesOrderCharge charge) => _charges.Add(charge);
}
