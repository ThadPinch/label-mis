using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Products;

public record ProductListItem(
    Guid Id,
    string InternalSku,
    string? CustomerSku,
    string Description,
    string CustomerNames,
    ProductStatus Status,
    bool IsActive);

public record RollSpecInput(
    int LabelsPerRoll,
    decimal CoreSizeIn,
    int UnwindPosition,
    decimal MaxOdIn,
    int RollsPerCase,
    string? CaseLabelFormat);

public record ProductFormInput(
    Guid? PrimaryCustomerId,
    IReadOnlyList<Guid> CustomerIds,
    string? CustomerSku,
    string Description,
    Guid? SourceEstimateLineId,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    Guid SubstrateId,
    InkSet InkSet,
    IReadOnlyList<FinishingOperationSelectionInput> FinishingOperations,
    Guid? DieId,
    string? ArtworkFilePath,
    RollSpecInput? RollSpec);

public record ProductPickerItem(
    Guid Id,
    string InternalSku,
    string? CustomerSku,
    string Description,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    Guid SubstrateId,
    InkSet InkSet,
    string FinishingOperationsJson,
    Guid? DieId);

public class ProductService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<ProductListItem>> ListAsync(
        string? search,
        Guid? customerId,
        ProductStatus? status,
        string? sort,
        int page,
        int pageSize,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Products.AsNoTracking()
            .Include(p => p.PrimaryCustomer)
            .Include(p => p.CustomerAssignments).ThenInclude(a => a.Customer)
            .AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (customerId.HasValue)
        {
            query = query.Where(p => p.CustomerAssignments.Any(a => a.CustomerId == customerId.Value));
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(p =>
                p.InternalSku.Contains(term)
                || (p.CustomerSku != null && p.CustomerSku.ToUpper().Contains(term))
                || p.Description.ToUpper().Contains(term));
        }

        query = sort switch
        {
            "sku" => query.OrderBy(p => p.InternalSku),
            "customer" => query.OrderBy(p => p.PrimaryCustomer != null ? p.PrimaryCustomer.Name : "").ThenBy(p => p.InternalSku),
            _ => query.OrderBy(p => p.InternalSku)
        };

        var total = await query.CountAsync(cancellationToken);
        var products = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = products
            .Select(p => new ProductListItem(
                p.Id,
                p.InternalSku,
                p.CustomerSku,
                p.Description,
                FormatCustomerNames(p),
                p.Status,
                p.IsActive))
            .ToList();

        return new PagedResult<ProductListItem>(items, total, page, pageSize);
    }

    public async Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Products
            .Include(p => p.RollSpec)
            .Include(p => p.PrimaryCustomer)
            .Include(p => p.CustomerAssignments).ThenInclude(a => a.Customer)
            .Include(p => p.Substrate)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Product> CreateFromEstimateLineAsync(Guid estimateLineId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var product = await EnsureProductForLineAsync(estimateLineId, userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<Product> EnsureProductForLineAsync(
        Guid estimateLineId,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await db.Products
            .FirstOrDefaultAsync(p => p.SourceEstimateLineId == estimateLineId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var line = await db.EstimateLines
            .Include(l => l.Estimate).ThenInclude(e => e.Customer)
            .SingleAsync(l => l.Id == estimateLineId, cancellationToken);

        if (line.Estimate.Status is not EstimateStatus.Won)
        {
            throw new InvalidOperationException("Only won estimates can produce products.");
        }

        if (line.SourceProductId is Guid sourceProductId)
        {
            return await db.Products.SingleAsync(p => p.Id == sourceProductId, cancellationToken);
        }

        var customerId = line.Estimate.CustomerId;
        var internalSku = await NextInternalSkuAsync(customerId, line.Estimate.Customer.Code, cancellationToken);
        var product = Product.Create(
            Guid.NewGuid(),
            customerId,
            [customerId],
            internalSku,
            customerSku: null,
            line.ProductDescription,
            estimateLineId,
            line.LabelAcrossIn,
            line.LabelAroundIn,
            line.CornerRadiusIn,
            line.SubstrateId,
            line.InkSet,
            line.FinishingOperationsJson,
            dieId: null,
            artworkFilePath: null,
            userId,
            now);

        db.Products.Add(product);
        return product;
    }

    public async Task<IReadOnlyList<ProductPickerItem>> ListPickerForCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default) =>
        await ListPickerAsync(query => query.Where(p => p.CustomerAssignments.Any(a => a.CustomerId == customerId)), cancellationToken);

    public async Task<IReadOnlyList<ProductPickerItem>> ListPickerAllAsync(
        CancellationToken cancellationToken = default) =>
        await ListPickerAsync(query => query, cancellationToken);

    public async Task<IReadOnlyList<ProductPickerItem>> ListPickerHouseAsync(
        CancellationToken cancellationToken = default) =>
        await ListPickerAsync(
            query => query.Where(p => p.PrimaryCustomerId == null && !p.CustomerAssignments.Any()),
            cancellationToken);

    public async Task<Product> CreateAsync(ProductFormInput input, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var primaryCustomerId = input.PrimaryCustomerId is { } pid && pid != Guid.Empty ? pid : (Guid?)null;
        string skuPrefix;
        if (primaryCustomerId is { } cid)
        {
            var customer = await db.Customers.AsNoTracking()
                .SingleAsync(c => c.Id == cid, cancellationToken);
            skuPrefix = customer.Code;
        }
        else
        {
            // Stock product with no customer affiliation.
            skuPrefix = "STK";
        }

        var internalSku = await NextInternalSkuAsync(primaryCustomerId, skuPrefix, cancellationToken);
        var product = Product.Create(
            Guid.NewGuid(),
            primaryCustomerId,
            input.CustomerIds,
            internalSku,
            input.CustomerSku,
            input.Description,
            input.SourceEstimateLineId,
            input.LabelAcrossIn,
            input.LabelAroundIn,
            input.CornerRadiusIn,
            input.SubstrateId,
            input.InkSet,
            EstimateCalculationMapper.SerializeFinishingOperations(input.FinishingOperations),
            input.DieId,
            input.ArtworkFilePath,
            userId,
            now);

        if (input.RollSpec is not null)
        {
            product.SetRollSpec(CreateRollSpec(product.Id, input.RollSpec, userId, now));
        }

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task UpdateAsync(Guid id, ProductFormInput input, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var product = await db.Products
            .Include(p => p.RollSpec)
            .Include(p => p.CustomerAssignments)
            .SingleAsync(p => p.Id == id, cancellationToken);

        product.Update(
            input.CustomerSku,
            input.Description,
            input.LabelAcrossIn,
            input.LabelAroundIn,
            input.CornerRadiusIn,
            input.SubstrateId,
            input.InkSet,
            EstimateCalculationMapper.SerializeFinishingOperations(input.FinishingOperations),
            input.DieId,
            input.ArtworkFilePath,
            userId,
            now);

        SyncCustomerAssignments(product, input.PrimaryCustomerId, input.CustomerIds, userId, now);

        if (input.RollSpec is null)
        {
            if (product.RollSpec is not null)
            {
                db.RollSpecs.Remove(product.RollSpec);
            }
        }
        else if (product.RollSpec is null)
        {
            var rollSpec = CreateRollSpec(product.Id, input.RollSpec, userId, now);
            product.SetRollSpec(rollSpec);
            db.RollSpecs.Add(rollSpec);
        }
        else
        {
            product.RollSpec.Update(
                input.RollSpec.LabelsPerRoll,
                input.RollSpec.CoreSizeIn,
                input.RollSpec.UnwindPosition,
                input.RollSpec.MaxOdIn,
                input.RollSpec.RollsPerCase,
                input.RollSpec.CaseLabelFormat,
                userId,
                now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DiscontinueAsync(Guid id, bool adminOverride, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var product = await db.Products.SingleAsync(p => p.Id == id, cancellationToken);
        var hasOpenLines = await HasOpenOrderLinesAsync(id, cancellationToken);

        if (hasOpenLines && !adminOverride)
        {
            throw new InvalidOperationException("Product cannot be discontinued while referenced by open sales orders.");
        }

        product.Discontinue(hasOpenLines, userId, now, adminOverride);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasOpenOrderLinesAsync(Guid productId, CancellationToken cancellationToken = default) =>
        await db.SalesOrderLines.AsNoTracking()
            .AnyAsync(l =>
                l.ProductId == productId
                && (l.SalesOrder.Status == SalesOrderStatus.Open
                    || l.SalesOrder.Status == SalesOrderStatus.InProduction),
                cancellationToken);

    public async Task<decimal?> GetDefaultUnitPriceAsync(
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Id == productId, cancellationToken);

        if (product.SourceEstimateLineId is null)
        {
            return null;
        }

        var breaks = await db.EstimateQuantityBreaks.AsNoTracking()
            .Where(q => q.EstimateLineId == product.SourceEstimateLineId)
            .ToListAsync(cancellationToken);

        if (breaks.Count == 0)
        {
            return null;
        }

        var exact = breaks.FirstOrDefault(q => q.Quantity == quantity);
        if (exact is not null)
        {
            return exact.UnitPrice;
        }

        var closest = breaks
            .OrderBy(q => Math.Abs(q.Quantity - quantity))
            .First();

        return closest.UnitPrice;
    }

    private async Task<IReadOnlyList<ProductPickerItem>> ListPickerAsync(
        Func<IQueryable<Product>, IQueryable<Product>> filter,
        CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Status == ProductStatus.Active);

        query = filter(query);

        return await query
            .OrderBy(p => p.InternalSku)
            .Select(p => new ProductPickerItem(
                p.Id,
                p.InternalSku,
                p.CustomerSku,
                p.Description,
                p.LabelAcrossIn,
                p.LabelAroundIn,
                p.CornerRadiusIn,
                p.SubstrateId,
                p.InkSet,
                p.FinishingOperationsJson,
                p.DieId))
            .ToListAsync(cancellationToken);
    }

    private void SyncCustomerAssignments(
        Product product,
        Guid? primaryCustomerId,
        IReadOnlyList<Guid> customerIds,
        Guid userId,
        DateTime now)
    {
        // Diff the assignments instead of clearing and re-adding: unchanged rows stay
        // put, only dropped rows are deleted and only new rows are inserted. Clearing and
        // re-adding would delete and re-insert unchanged (ProductId, CustomerId) pairs,
        // colliding on the unique index (DbUpdateConcurrencyException). New rows must be
        // added to the DbSet explicitly — children reached via the tracked product's
        // navigation carry app-assigned Guid keys that EF would otherwise try to UPDATE.
        var (addedAssignments, removedAssignments) = product.SetCustomers(primaryCustomerId, customerIds, userId, now);
        db.ProductCustomers.AddRange(addedAssignments);
        db.ProductCustomers.RemoveRange(removedAssignments);
    }

    private static string FormatCustomerNames(Product product) =>
        string.Join(", ", product.CustomerAssignments
            .Select(a => a.Customer?.Name ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name));

    private async Task<string> NextInternalSkuAsync(
        Guid? customerId,
        string customerCode,
        CancellationToken cancellationToken)
    {
        var count = customerId is { } cid
            ? await db.Products.CountAsync(p => p.PrimaryCustomerId == cid, cancellationToken)
            : await db.Products.CountAsync(p => p.PrimaryCustomerId == null, cancellationToken);
        return $"{customerCode.Trim().ToUpperInvariant()}-{(count + 1):D4}";
    }

    private static RollSpec CreateRollSpec(Guid productId, RollSpecInput input, Guid userId, DateTime now) =>
        RollSpec.Create(
            Guid.NewGuid(),
            productId,
            input.LabelsPerRoll,
            input.CoreSizeIn,
            input.UnwindPosition,
            input.MaxOdIn,
            input.RollsPerCase,
            input.CaseLabelFormat,
            userId,
            now);

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
