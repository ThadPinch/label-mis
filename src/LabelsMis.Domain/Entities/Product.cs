using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Product : MasterDataEntity
{
    private readonly List<ProductCustomer> _customerAssignments = [];
    private RollSpec? _rollSpec;

    private Product()
    {
    }

    public Guid PrimaryCustomerId { get; private set; }
    public Customer PrimaryCustomer { get; private set; } = null!;
    public string? CustomerSku { get; private set; }
    public string InternalSku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid? SourceEstimateLineId { get; private set; }
    public EstimateLine? SourceEstimateLine { get; private set; }
    public decimal LabelAcrossIn { get; private set; }
    public decimal LabelAroundIn { get; private set; }
    public decimal CornerRadiusIn { get; private set; }
    public Guid SubstrateId { get; private set; }
    public Stock Substrate { get; private set; } = null!;
    public InkSet InkSet { get; private set; }
    public string FinishingOperationsJson { get; private set; } = "[]";
    public Guid? DieId { get; private set; }
    public Die? Die { get; private set; }
    public string? ArtworkFilePath { get; private set; }
    public ProductStatus Status { get; private set; }

    public IReadOnlyCollection<ProductCustomer> CustomerAssignments => _customerAssignments;
    public RollSpec? RollSpec => _rollSpec;

    public static Product Create(
        Guid id,
        Guid primaryCustomerId,
        IEnumerable<Guid> customerIds,
        string internalSku,
        string? customerSku,
        string description,
        Guid? sourceEstimateLineId,
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        Guid substrateId,
        InkSet inkSet,
        string finishingOperationsJson,
        Guid? dieId,
        string? artworkFilePath,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(internalSku))
        {
            throw new ArgumentException("Internal SKU is required.", nameof(internalSku));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is required.", nameof(description));
        }

        var distinctCustomerIds = NormalizeCustomerIds(primaryCustomerId, customerIds);

        var product = new Product
        {
            PrimaryCustomerId = primaryCustomerId,
            InternalSku = internalSku.Trim().ToUpperInvariant(),
            CustomerSku = string.IsNullOrWhiteSpace(customerSku) ? null : customerSku.Trim(),
            Description = description.Trim(),
            SourceEstimateLineId = sourceEstimateLineId,
            LabelAcrossIn = labelAcrossIn,
            LabelAroundIn = labelAroundIn,
            CornerRadiusIn = cornerRadiusIn,
            SubstrateId = substrateId,
            InkSet = inkSet,
            FinishingOperationsJson = string.IsNullOrWhiteSpace(finishingOperationsJson) ? "[]" : finishingOperationsJson,
            DieId = dieId,
            ArtworkFilePath = string.IsNullOrWhiteSpace(artworkFilePath) ? null : artworkFilePath.Trim(),
            Status = ProductStatus.Active
        };
        product.SetCreated(id, createdById, createdAt);
        product.ReplaceCustomerAssignments(distinctCustomerIds, createdById, createdAt);
        return product;
    }

    public void Update(
        string? customerSku,
        string description,
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        Guid substrateId,
        InkSet inkSet,
        string finishingOperationsJson,
        Guid? dieId,
        string? artworkFilePath,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is required.", nameof(description));
        }

        CustomerSku = string.IsNullOrWhiteSpace(customerSku) ? null : customerSku.Trim();
        Description = description.Trim();
        LabelAcrossIn = labelAcrossIn;
        LabelAroundIn = labelAroundIn;
        CornerRadiusIn = cornerRadiusIn;
        SubstrateId = substrateId;
        InkSet = inkSet;
        FinishingOperationsJson = string.IsNullOrWhiteSpace(finishingOperationsJson) ? "[]" : finishingOperationsJson;
        DieId = dieId;
        ArtworkFilePath = string.IsNullOrWhiteSpace(artworkFilePath) ? null : artworkFilePath.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void SetCustomers(
        Guid primaryCustomerId,
        IEnumerable<Guid> customerIds,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        var distinctCustomerIds = NormalizeCustomerIds(primaryCustomerId, customerIds);
        PrimaryCustomerId = primaryCustomerId;
        ReplaceCustomerAssignments(distinctCustomerIds, modifiedById, modifiedAt);
        SetModified(modifiedById, modifiedAt);
    }

    public void SetRollSpec(RollSpec rollSpec) => _rollSpec = rollSpec;

    public void Discontinue(bool hasOpenOrderLines, Guid modifiedById, DateTime modifiedAt, bool allowWithOpenOrders = false)
    {
        if (hasOpenOrderLines && !allowWithOpenOrders)
        {
            throw new InvalidOperationException("Product cannot be discontinued while referenced by open sales orders.");
        }

        Status = ProductStatus.Discontinued;
        Deactivate(modifiedById, modifiedAt);
    }

    public void ReactivateProduct(Guid modifiedById, DateTime modifiedAt)
    {
        Status = ProductStatus.Active;
        Reactivate(modifiedById, modifiedAt);
    }

    private void ReplaceCustomerAssignments(IReadOnlyList<Guid> customerIds, Guid userId, DateTime timestamp)
    {
        _customerAssignments.Clear();
        foreach (var customerId in customerIds)
        {
            _customerAssignments.Add(ProductCustomer.Create(Guid.NewGuid(), Id, customerId, userId, timestamp));
        }
    }

    private static IReadOnlyList<Guid> NormalizeCustomerIds(Guid primaryCustomerId, IEnumerable<Guid> customerIds)
    {
        var distinctCustomerIds = customerIds
            .Append(primaryCustomerId)
            .Distinct()
            .ToList();

        if (distinctCustomerIds.Count == 0)
        {
            throw new ArgumentException("At least one customer is required.", nameof(customerIds));
        }

        if (!distinctCustomerIds.Contains(primaryCustomerId))
        {
            throw new ArgumentException("Primary customer must be included in customer assignments.", nameof(primaryCustomerId));
        }

        return distinctCustomerIds;
    }
}
