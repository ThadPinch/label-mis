using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class ShippingMethod : MasterDataEntity
{
    public static readonly Guid PickupId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    public static readonly Guid DeliveryId = Guid.Parse("22222222-2222-2222-2222-222222222202");
    public static readonly Guid FedexId = Guid.Parse("22222222-2222-2222-2222-222222222203");

    private ShippingMethod()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public ShippingMethodType MethodType { get; private set; }
    public decimal Price { get; private set; }
    public bool RequiresAddress { get; private set; } = true;

    public static ShippingMethod Create(
        Guid id,
        string name,
        ShippingMethodType methodType,
        decimal price,
        bool requiresAddress,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(name, price);

        var method = new ShippingMethod
        {
            Name = name.Trim(),
            MethodType = methodType,
            Price = price,
            RequiresAddress = requiresAddress
        };
        method.SetCreated(id, createdById, createdAt);
        return method;
    }

    public void Update(
        string name,
        ShippingMethodType methodType,
        decimal price,
        bool requiresAddress,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        Validate(name, price);

        Name = name.Trim();
        MethodType = methodType;
        Price = price;
        RequiresAddress = requiresAddress;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Default shipping methods seeded on first run. FedEx is a marker only — no live rating.</summary>
    public static IEnumerable<ShippingMethod> CreateDefaults(Guid createdById, DateTime createdAt) =>
    [
        Create(PickupId, "Pickup", ShippingMethodType.Pickup, 0m, requiresAddress: false, createdById, createdAt),
        Create(DeliveryId, "Delivery", ShippingMethodType.Delivery, 25m, requiresAddress: true, createdById, createdAt),
        Create(FedexId, "FedEx", ShippingMethodType.Fedex, 35m, requiresAddress: true, createdById, createdAt)
    ];

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
