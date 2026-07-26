using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

public class Product : MasterDataEntity
{
    private readonly List<ProductCustomer> _customerAssignments = [];
    private RollSpec? _rollSpec;

    private Product()
    {
    }

    public Guid? PrimaryCustomerId { get; private set; }
    public Customer? PrimaryCustomer { get; private set; }
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

    /// <summary>The original file name of the uploaded artwork, for display; the stored key in
    /// <see cref="ArtworkFilePath"/> is timestamped and opaque.</summary>
    public string? ArtworkOriginalFileName { get; private set; }
    public ProductStatus Status { get; private set; }

    public IReadOnlyCollection<ProductCustomer> CustomerAssignments => _customerAssignments;
    public RollSpec? RollSpec => _rollSpec;

    public static Product Create(
        Guid id,
        Guid? primaryCustomerId,
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
            PrimaryCustomerId = primaryCustomerId is { } pc && pc != Guid.Empty ? pc : null,
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

    /// <summary>Points the product at a newly uploaded artwork file.</summary>
    public void SetArtworkFile(string storageKey, string? originalFileName, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        ArtworkFilePath = storageKey.Trim();
        ArtworkOriginalFileName = string.IsNullOrWhiteSpace(originalFileName) ? null : originalFileName.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Diffs the customer assignments to the requested set and reports which rows were
    /// added and removed so the caller can stage the corresponding inserts/deletes with EF. The
    /// caller must add <paramref name="added"/> to the DbSet: children reached through a tracked
    /// parent's navigation carry app-assigned Guid keys, which EF change detection would otherwise
    /// mistake for existing rows and try to UPDATE (affecting 0 rows). See ProductService.UpdateAsync.</summary>
    public (IReadOnlyList<ProductCustomer> Added, IReadOnlyList<ProductCustomer> Removed) SetCustomers(
        Guid? primaryCustomerId,
        IEnumerable<Guid> customerIds,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        var distinctCustomerIds = NormalizeCustomerIds(primaryCustomerId, customerIds);
        PrimaryCustomerId = primaryCustomerId is { } pc && pc != Guid.Empty ? pc : null;
        var (added, removed) = ReplaceCustomerAssignments(distinctCustomerIds, modifiedById, modifiedAt);
        SetModified(modifiedById, modifiedAt);
        return (added, removed);
    }

    /// <summary>Seeds a label spec from this product's template fields. Gutters, bleed, white, spots,
    /// waste, and layout aren't held on the product, so they start at their defaults.</summary>
    public LabelSpec ToLabelSpec() => LabelSpec.Create(
        LabelAcrossIn, LabelAroundIn, CornerRadiusIn,
        gutterAcrossIn: 0m, gutterAroundIn: 0m, bleedIn: 0m,
        SubstrateId, DieId, InkSet,
        whiteHits: 0, whiteCoveragePct: 1m, spotsJson: "[]", FinishingOperationsJson,
        setupWasteImpressions: 0m, runningWastePct: 0m,
        maxLabelsAcrossOverride: null, labelOrientationOverride: null, ArtworkFilePath);

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

    private (IReadOnlyList<ProductCustomer> Added, IReadOnlyList<ProductCustomer> Removed) ReplaceCustomerAssignments(
        IReadOnlyList<Guid> customerIds, Guid userId, DateTime timestamp)
    {
        // Diff against the current assignments rather than clearing and re-adding.
        // Recreating rows for customers that are unchanged makes EF delete and
        // re-insert the same (ProductId, CustomerId), which collides on the unique
        // index and throws DbUpdateConcurrencyException. Keep existing rows, remove
        // only those dropped, and add only those newly assigned.
        var desired = customerIds.ToHashSet();
        var removed = _customerAssignments.Where(a => !desired.Contains(a.CustomerId)).ToList();
        _customerAssignments.RemoveAll(a => !desired.Contains(a.CustomerId));

        var existing = _customerAssignments.Select(a => a.CustomerId).ToHashSet();
        var added = new List<ProductCustomer>();
        foreach (var customerId in customerIds)
        {
            if (existing.Add(customerId))
            {
                var assignment = ProductCustomer.Create(Guid.NewGuid(), Id, customerId, userId, timestamp);
                _customerAssignments.Add(assignment);
                added.Add(assignment);
            }
        }

        return (added, removed);
    }

    private static IReadOnlyList<Guid> NormalizeCustomerIds(Guid? primaryCustomerId, IEnumerable<Guid> customerIds)
    {
        var ids = customerIds.Where(id => id != Guid.Empty);
        if (primaryCustomerId is { } primary && primary != Guid.Empty)
        {
            ids = ids.Append(primary);
        }

        // May be empty: a product can exist without any customer affiliation.
        return ids.Distinct().ToList();
    }
}
