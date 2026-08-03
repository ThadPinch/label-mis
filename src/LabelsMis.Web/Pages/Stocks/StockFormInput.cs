using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services.Stocks;

namespace LabelsMis.Web.Pages.Stocks;

public class StockFormInput : IValidatableObject
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Stock type")]
    public StockType StockType { get; set; } = StockType.Substrate;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Face material")]
    public string FaceMaterial { get; set; } = string.Empty;

    [Required]
    public string Adhesive { get; set; } = string.Empty;

    public string Liner { get; set; } = string.Empty;

    [Range(0.0001, 1000)]
    [Display(Name = "Shrink layflat (in)")]
    public decimal? ShrinkLayflatIn { get; set; }

    [Range(0.0001, 100)]
    [Display(Name = "Total caliper (mil)")]
    public decimal TotalCaliperMil { get; set; }

    [Range(0.0001, 100)]
    [Display(Name = "Width (in)")]
    public decimal WidthIn { get; set; }

    [Required]
    [Display(Name = "Supplier")]
    public Guid SupplierId { get; set; }

    [Display(Name = "Supplier part number")]
    public string? SupplierPartNumber { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Cost per MSI")]
    public decimal CostPerMsi { get; set; }

    [Range(0, 999999999)]
    [Display(Name = "Re-Order Quantity")]
    public decimal MinOrderQtyLf { get; set; }

    // Shrink stocks have no liner; other types have no layflat.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StockType == StockType.Shrink && ShrinkLayflatIn is null)
        {
            yield return new ValidationResult("Shrink layflat is required for shrink stocks.", [nameof(ShrinkLayflatIn)]);
        }

        if (StockType != StockType.Shrink && string.IsNullOrWhiteSpace(Liner))
        {
            yield return new ValidationResult("The Liner field is required.", [nameof(Liner)]);
        }
    }

    public StockForm ToForm() => new(
        Code,
        Description,
        FaceMaterial,
        Adhesive,
        StockType == StockType.Shrink ? string.Empty : Liner,
        TotalCaliperMil,
        WidthIn,
        SupplierId,
        SupplierPartNumber,
        CostPerMsi,
        MinOrderQtyLf,
        StockType,
        ShrinkLayflatIn);

    public static StockFormInput FromEntity(Domain.Entities.Stock stock) => new()
    {
        Code = stock.Code,
        StockType = stock.StockType,
        Description = stock.Description,
        FaceMaterial = stock.FaceMaterial,
        Adhesive = stock.Adhesive,
        Liner = stock.Liner,
        ShrinkLayflatIn = stock.ShrinkLayflatIn,
        TotalCaliperMil = stock.TotalCaliperMil,
        WidthIn = stock.WidthIn,
        SupplierId = stock.SupplierId,
        SupplierPartNumber = stock.SupplierPartNumber,
        CostPerMsi = stock.CostPerMsi,
        MinOrderQtyLf = stock.MinOrderQtyLf
    };

    public static StockFormInput ForDuplicate(Domain.Entities.Stock stock)
    {
        var input = FromEntity(stock);
        input.Code = MasterDataDuplicateHelper.DuplicateCode(stock.Code);
        return input;
    }
}
