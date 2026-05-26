using System.ComponentModel.DataAnnotations;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services.Stocks;

namespace LabelsMis.Web.Pages.Stocks;

public class StockFormInput
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Face material")]
    public string FaceMaterial { get; set; } = string.Empty;

    [Required]
    public string Adhesive { get; set; } = string.Empty;

    [Required]
    public string Liner { get; set; } = string.Empty;

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
    [Display(Name = "Min order qty (LF)")]
    public decimal MinOrderQtyLf { get; set; }

    public StockForm ToForm() => new(
        Code,
        Description,
        FaceMaterial,
        Adhesive,
        Liner,
        TotalCaliperMil,
        WidthIn,
        SupplierId,
        SupplierPartNumber,
        CostPerMsi,
        MinOrderQtyLf);

    public static StockFormInput FromEntity(Domain.Entities.Stock stock) => new()
    {
        Code = stock.Code,
        Description = stock.Description,
        FaceMaterial = stock.FaceMaterial,
        Adhesive = stock.Adhesive,
        Liner = stock.Liner,
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
