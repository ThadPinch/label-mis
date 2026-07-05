using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Rolls;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Rolls;

[Authorize(Policy = TransactionPolicies.InventoryEdit)]
public class AddModel(RollService rollService, LabelsMisDbContext db) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public record StockOption(Guid Id, string Label, StockType StockType);

    public IReadOnlyList<StockOption> StockOptions { get; private set; } = [];

    public class InputModel
    {
        [Required]
        [Display(Name = "Stock")]
        public Guid StockId { get; set; }

        [Required]
        [Display(Name = "Supplier lot number")]
        [StringLength(100)]
        public string SupplierLotNumber { get; set; } = string.Empty;

        [Display(Name = "Width (in)")]
        [Range(0.01, 999, ErrorMessage = "Width must be greater than 0.")]
        public decimal WidthIn { get; set; }

        [Display(Name = "Length (LF)")]
        [Range(0.01, 1_000_000, ErrorMessage = "Length must be greater than 0.")]
        public decimal LengthLf { get; set; }

        [Display(Name = "Location")]
        [StringLength(100)]
        public string? Location { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadStockOptionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadStockOptionsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var id = await rollService.AddManualAsync(
                new ManualRollInput(
                    Input.StockId,
                    Input.SupplierLotNumber,
                    Input.WidthIn,
                    Input.LengthLf,
                    Input.Location),
                cancellationToken);
            return RedirectToPage("Detail", new { id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadStockOptionsAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadStockOptionsAsync(CancellationToken cancellationToken)
    {
        StockOptions = await db.Stocks.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Code)
            .Select(s => new StockOption(s.Id, $"{s.Code} — {s.Description}", s.StockType))
            .ToListAsync(cancellationToken);
    }
}
