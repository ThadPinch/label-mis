using LabelsMis.Domain.Common;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

/// <summary>A flat, non-label item quoted on an estimate: one-time charges (die creation, design
/// time, plates) and outsourced goods (promo items, print, wide format). Charges carry through to the
/// sales order and invoice; they never become production jobs, but an outsourced charge is tracked
/// and received on the production Outsourced page.</summary>
public class EstimateCharge : EntityBase
{
    private EstimateCharge()
    {
    }

    public Guid EstimateId { get; private set; }
    public Estimate Estimate { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }

    /// <summary>Bought from an outside vendor; the price stays what we quote, <see cref="OutsourceCost"/>
    /// is what the vendor charges us for the whole line.</summary>
    public bool IsOutsourced { get; private set; }
    public Guid? OutsourceVendorId { get; private set; }
    public Supplier? OutsourceVendor { get; private set; }
    public string? OutsourceQuoteNumber { get; private set; }
    public decimal? OutsourceCost { get; private set; }
    public DateOnly? OutsourceExpectedIn { get; private set; }
    /// <summary>Internal only — never printed on customer documents.</summary>
    public string? OutsourcePrivateNotes { get; private set; }

    /// <summary>The vendor details as a value, or null when the charge is not outsourced.</summary>
    public OutsourceDetails? OutsourceDetails => IsOutsourced
        ? new OutsourceDetails(OutsourceVendorId, OutsourceQuoteNumber, OutsourceExpectedIn, OutsourcePrivateNotes)
        : null;

    public static EstimateCharge Create(
        Guid id,
        Guid estimateId,
        int lineNumber,
        string description,
        int quantity,
        decimal unitPrice,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(lineNumber, description, quantity, unitPrice);

        var charge = new EstimateCharge
        {
            EstimateId = estimateId,
            LineNumber = lineNumber,
            Description = description.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity
        };
        charge.SetCreated(id, createdById, createdAt);
        return charge;
    }

    /// <summary>Marks the charge as outsourced (vendor details + vendor cost for the line), or clears
    /// outsourcing when <paramref name="details"/> is null.</summary>
    public void SetOutsource(OutsourceDetails? details, decimal? vendorCost, Guid modifiedById, DateTime modifiedAt)
    {
        if (details is not null && vendorCost is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vendorCost), "Vendor cost cannot be negative.");
        }

        var normalized = details?.Normalize();
        IsOutsourced = normalized is not null;
        OutsourceVendorId = normalized?.VendorId;
        OutsourceQuoteNumber = normalized?.QuoteNumber;
        OutsourceCost = normalized is null ? null : vendorCost;
        OutsourceExpectedIn = normalized?.ExpectedIn;
        OutsourcePrivateNotes = normalized?.PrivateNotes;
        SetModified(modifiedById, modifiedAt);
    }

    private static void Validate(int lineNumber, string description, int quantity, decimal unitPrice)
    {
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line number must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Charge description is required.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }
    }
}
