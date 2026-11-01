namespace LabelsMis.Domain.Shipping;

/// <summary>Where a draft invoice's shipping amount came from.</summary>
public enum InvoiceShippingSource
{
    /// <summary>Nothing has shipped yet: the shipping quoted on the order.</summary>
    Quote = 0,

    /// <summary>The sum of the order's shipments' billed shipping.</summary>
    Shipments = 1,

    /// <summary>A shipment's billed shipping is still unknown (carrier cost not entered): the
    /// invoice keeps its current amount until it is.</summary>
    Pending = 2,

    /// <summary>Set by hand on the invoice; wins over everything else.</summary>
    Manual = 3
}

/// <summary>One shipped shipment's billed shipping (null = carrier cost not entered yet).</summary>
public sealed record ShippedShipmentBilling(string ShipmentNumber, decimal? BilledShipping);

public sealed record InvoiceShippingResolution(
    decimal Amount,
    InvoiceShippingSource Source,
    IReadOnlyList<string> PendingShipmentNumbers)
{
    public bool IsPending => Source == InvoiceShippingSource.Pending;
}

/// <summary>What a draft invoice bills for shipping: a manual override when set; before anything
/// ships, the shipping quoted on the order; once shipments exist, the sum of their billed shipping.
/// While any shipped shipment's billed shipping is unknown the invoice keeps its current amount and
/// is pending (it isn't ready to bill until the carrier cost is entered).</summary>
public static class InvoiceShippingRules
{
    public static InvoiceShippingResolution Resolve(
        decimal? manualOverride,
        decimal quotedShipping,
        decimal currentAmount,
        IReadOnlyCollection<ShippedShipmentBilling> shippedShipments)
    {
        if (manualOverride is { } manual)
        {
            return new InvoiceShippingResolution(manual, InvoiceShippingSource.Manual, []);
        }

        if (shippedShipments.Count == 0)
        {
            return new InvoiceShippingResolution(quotedShipping, InvoiceShippingSource.Quote, []);
        }

        var pending = shippedShipments
            .Where(s => s.BilledShipping is null)
            .Select(s => s.ShipmentNumber)
            .ToList();
        if (pending.Count > 0)
        {
            return new InvoiceShippingResolution(currentAmount, InvoiceShippingSource.Pending, pending);
        }

        return new InvoiceShippingResolution(
            shippedShipments.Sum(s => s.BilledShipping!.Value),
            InvoiceShippingSource.Shipments,
            []);
    }
}
