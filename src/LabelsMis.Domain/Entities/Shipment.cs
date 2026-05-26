using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Shipment : EntityBase
{
    private readonly List<ShipmentPackage> _packages = [];
    private readonly List<ShipmentLine> _lines = [];

    private Shipment()
    {
    }

    public string ShipmentNumber { get; private set; } = string.Empty;
    public Guid SalesOrderId { get; private set; }
    public SalesOrder SalesOrder { get; private set; } = null!;
    public DateOnly ShipDate { get; private set; }
    public Carrier Carrier { get; private set; }
    public FedexServiceLevel ServiceLevel { get; private set; }
    public Guid ShipFromAddressId { get; private set; }
    public Address ShipFromAddress { get; private set; } = null!;
    public Guid ShipToAddressId { get; private set; }
    public Address ShipToAddress { get; private set; } = null!;
    public ShipmentStatus Status { get; private set; }
    public decimal TotalDeclaredValue { get; private set; }
    public BillingType BillingType { get; private set; }
    public string? BillingAccountNumber { get; private set; }
    public decimal TotalShippingCost { get; private set; }

    public IReadOnlyCollection<ShipmentPackage> Packages => _packages;
    public IReadOnlyCollection<ShipmentLine> Lines => _lines;

    public static Shipment CreatePending(
        Guid id,
        string shipmentNumber,
        Guid salesOrderId,
        DateOnly shipDate,
        Carrier carrier,
        FedexServiceLevel serviceLevel,
        Guid shipFromAddressId,
        Guid shipToAddressId,
        decimal totalDeclaredValue,
        BillingType billingType,
        string? billingAccountNumber,
        Guid createdById,
        DateTime createdAt)
    {
        var shipment = new Shipment
        {
            ShipmentNumber = shipmentNumber,
            SalesOrderId = salesOrderId,
            ShipDate = shipDate,
            Carrier = carrier,
            ServiceLevel = serviceLevel,
            ShipFromAddressId = shipFromAddressId,
            ShipToAddressId = shipToAddressId,
            Status = ShipmentStatus.Pending,
            TotalDeclaredValue = totalDeclaredValue,
            BillingType = billingType,
            BillingAccountNumber = string.IsNullOrWhiteSpace(billingAccountNumber) ? null : billingAccountNumber.Trim()
        };
        shipment.SetCreated(id, createdById, createdAt);
        return shipment;
    }

    public void AddPackage(ShipmentPackage package)
    {
        if (_packages.Count == 0 && package.PackageNumber != 1)
        {
            throw new InvalidOperationException("First package must be number 1.");
        }

        _packages.Add(package);
    }

    public void ReplacePackages(IEnumerable<ShipmentPackage> packages)
    {
        var list = packages.ToList();
        if (list.Count == 0)
        {
            throw new InvalidOperationException("Shipment must have at least one package.");
        }

        _packages.Clear();
        _packages.AddRange(list);
    }

    public void AddLine(ShipmentLine line) => _lines.Add(line);

    public void ReplaceLines(IEnumerable<ShipmentLine> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
    }

    public void MarkInTransit(decimal shippingCost, Guid modifiedById, DateTime modifiedAt)
    {
        if (_packages.Count == 0)
        {
            throw new InvalidOperationException("Cannot ship without packages.");
        }

        if (_packages.Any(p => string.IsNullOrWhiteSpace(p.TrackingNumber)))
        {
            throw new InvalidOperationException("All packages must have tracking numbers.");
        }

        Status = ShipmentStatus.InTransit;
        TotalShippingCost = shippingCost;
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkDelivered(Guid modifiedById, DateTime modifiedAt)
    {
        Status = ShipmentStatus.Delivered;
        SetModified(modifiedById, modifiedAt);
    }
}
