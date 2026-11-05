using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class ShippingMethod : MasterDataEntity
{
    public static readonly Guid PickupId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    public static readonly Guid DeliveryId = Guid.Parse("22222222-2222-2222-2222-222222222202");
    public static readonly Guid FedexId = Guid.Parse("22222222-2222-2222-2222-222222222203");
    public static readonly Guid CustomerAccountId = Guid.Parse("22222222-2222-2222-2222-222222222204");

    public const string FedexNotes = "estimate — replaced by actual × markup at shipment";
    public const string CustomerAccountNotes = "Knight Transportation — bills to customer, $0";
    public const int NotesMaxLength = 500;

    private ShippingMethod()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public ShippingMethodType MethodType { get; private set; }
    public decimal Price { get; private set; }
    public bool RequiresAddress { get; private set; } = true;

    /// <summary>How the method bills, shown next to it on estimates, orders and in Setup
    /// (e.g. FedEx's price is only an estimate until the carrier cost is entered).</summary>
    public string? Notes { get; private set; }

    /// <summary>Orders on this method are marked shipped only by recording the shipment (packages,
    /// tracking, carrier cost) on Shipments → Record shipment; production's Mark shipped is refused.
    /// Off, Mark shipped records a package-less shipment by itself (pickup, local delivery, customer
    /// account), so the order and its invoice still see it shipped.</summary>
    public bool RequiresShipmentRecord { get; private set; }

    /// <summary>The flag a method gets when none is chosen: carrier methods need the shipment record
    /// (their billed shipping comes from its carrier cost); hand-offs don't.</summary>
    public static bool DefaultRequiresShipmentRecord(ShippingMethodType methodType) =>
        methodType == ShippingMethodType.Fedex;

    public static ShippingMethod Create(
        Guid id,
        string name,
        ShippingMethodType methodType,
        decimal price,
        bool requiresAddress,
        Guid createdById,
        DateTime createdAt,
        string? notes = null,
        bool? requiresShipmentRecord = null)
    {
        Validate(name, price);

        var method = new ShippingMethod
        {
            Name = name.Trim(),
            MethodType = methodType,
            Price = price,
            RequiresAddress = requiresAddress,
            Notes = NormalizeNotes(notes),
            RequiresShipmentRecord = requiresShipmentRecord ?? DefaultRequiresShipmentRecord(methodType)
        };
        method.SetCreated(id, createdById, createdAt);
        return method;
    }

    public void Update(
        string name,
        ShippingMethodType methodType,
        decimal price,
        bool requiresAddress,
        bool requiresShipmentRecord,
        string? notes,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        Validate(name, price);

        Name = name.Trim();
        MethodType = methodType;
        Price = price;
        RequiresAddress = requiresAddress;
        RequiresShipmentRecord = requiresShipmentRecord;
        Notes = NormalizeNotes(notes);
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Default shipping methods seeded on first run. FedEx is a marker only — no live rating.
    /// Existing databases get the Customer account method and the FedEx note from the
    /// AddShippingMethodNotes migration instead (the seeder only seeds an empty table).</summary>
    public static IEnumerable<ShippingMethod> CreateDefaults(Guid createdById, DateTime createdAt) =>
    [
        Create(PickupId, "Pickup", ShippingMethodType.Pickup, 0m, requiresAddress: false, createdById, createdAt),
        Create(DeliveryId, "Delivery", ShippingMethodType.Delivery, 25m, requiresAddress: true, createdById, createdAt),
        Create(FedexId, "FedEx", ShippingMethodType.Fedex, 35m, requiresAddress: true, createdById, createdAt, FedexNotes),
        Create(CustomerAccountId, "Customer account", ShippingMethodType.CustomerAccount, 0m, requiresAddress: true,
            createdById, createdAt, CustomerAccountNotes)
    ];

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var trimmed = notes.Trim();
        if (trimmed.Length > NotesMaxLength)
        {
            throw new ArgumentException($"Notes must be {NotesMaxLength} characters or fewer.", nameof(notes));
        }

        return trimmed;
    }

    private static void Validate(string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Shipping method name is required.", nameof(name));
        }

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Shipping price cannot be negative.");
        }
    }
}
