using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Stock : MasterDataEntity
{
    private readonly List<StockCostHistory> _costHistory = [];

    private Stock()
    {
    }

    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string FaceMaterial { get; private set; } = string.Empty;
    public string Adhesive { get; private set; } = string.Empty;
    public string Liner { get; private set; } = string.Empty;
    public decimal TotalCaliperMil { get; private set; }
    public decimal WidthIn { get; private set; }
    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public string? SupplierPartNumber { get; private set; }
    public decimal CostPerMsi { get; private set; }
    public decimal MinOrderQtyLf { get; private set; }
    public StockType StockType { get; private set; } = StockType.Substrate;

    public IReadOnlyCollection<StockCostHistory> CostHistory => _costHistory;

    public static Stock Create(
        Guid id,
        string code,
        string description,
        string faceMaterial,
        string adhesive,
        string liner,
        decimal totalCaliperMil,
        decimal widthIn,
        Guid supplierId,
        string? supplierPartNumber,
        decimal costPerMsi,
        decimal minOrderQtyLf,
        Guid createdById,
        DateTime createdAt,
        StockType stockType = StockType.Substrate)
    {
        Validate(code, description, widthIn, costPerMsi, minOrderQtyLf);

        var stock = new Stock
        {
            StockType = stockType,
            Code = code.Trim().ToUpperInvariant(),
            Description = description.Trim(),
            FaceMaterial = faceMaterial.Trim(),
            Adhesive = adhesive.Trim(),
            Liner = liner.Trim(),
            TotalCaliperMil = totalCaliperMil,
            WidthIn = widthIn,
            SupplierId = supplierId,
            SupplierPartNumber = string.IsNullOrWhiteSpace(supplierPartNumber) ? null : supplierPartNumber.Trim(),
            CostPerMsi = costPerMsi,
            MinOrderQtyLf = minOrderQtyLf
        };
        stock.SetCreated(id, createdById, createdAt);
        return stock;
    }

    public void Update(
        string code,
        string description,
        string faceMaterial,
        string adhesive,
        string liner,
        decimal totalCaliperMil,
        decimal widthIn,
        Guid supplierId,
        string? supplierPartNumber,
        decimal costPerMsi,
        decimal minOrderQtyLf,
        Guid modifiedById,
        DateTime modifiedAt,
        StockType stockType = StockType.Substrate)
    {
        Validate(code, description, widthIn, costPerMsi, minOrderQtyLf);

        StockType = stockType;
        Code = code.Trim().ToUpperInvariant();
        Description = description.Trim();
        FaceMaterial = faceMaterial.Trim();
        Adhesive = adhesive.Trim();
        Liner = liner.Trim();
        TotalCaliperMil = totalCaliperMil;
        WidthIn = widthIn;
        SupplierId = supplierId;
        SupplierPartNumber = string.IsNullOrWhiteSpace(supplierPartNumber) ? null : supplierPartNumber.Trim();
        CostPerMsi = costPerMsi;
        MinOrderQtyLf = minOrderQtyLf;
        SetModified(modifiedById, modifiedAt);
    }

    public StockCostHistory RecordCostChange(
        Guid historyId,
        decimal costPerMsi,
        DateTime effectiveDate,
        Guid recordedById,
        DateTime recordedAt)
    {
        if (costPerMsi < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costPerMsi), "Cost per MSI cannot be negative.");
        }

        CostPerMsi = costPerMsi;
        SetModified(recordedById, recordedAt);

        var history = StockCostHistory.Create(
            historyId,
            Id,
            costPerMsi,
            effectiveDate,
            recordedById,
            recordedAt);
        _costHistory.Add(history);
        return history;
    }

    private static void Validate(
        string code,
        string description,
        decimal widthIn,
        decimal costPerMsi,
        decimal minOrderQtyLf)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Stock code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Stock description is required.", nameof(description));
        }

        if (widthIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthIn), "Width must be greater than zero.");
        }

        if (costPerMsi < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costPerMsi), "Cost per MSI cannot be negative.");
        }

        if (minOrderQtyLf < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minOrderQtyLf), "Minimum order quantity cannot be negative.");
        }
    }

    public void AddCostHistory(StockCostHistory history) => _costHistory.Add(history);
}
