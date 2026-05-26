using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.SalesOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Estimates;

public class ConvertLinePageInput
{
    [Required]
    public Guid EstimateLineId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 0;

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    public string? LineNotes { get; set; }
}

[Authorize(Policy = TransactionPolicies.SalesOrdersEdit)]
public class ConvertModel(
    EstimateService estimateService,
    SalesOrderService salesOrderService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public string? CustomerPoNumber { get; set; }

    [BindProperty]
    public DateOnly? RequestedShipDate { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    [BindProperty]
    public List<ConvertLinePageInput> Lines { get; set; } = [];

    public EstimateDetail? Detail { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Detail = await estimateService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();
        if (Detail.Estimate.Status is not EstimateStatus.Won)
        {
            return RedirectToPage("Edit", new { id = Id });
        }
        if (Detail.SalesOrderId is not null)
        {
            return RedirectToPage("/SalesOrders/Edit", new { id = Detail.SalesOrderId });
        }

        Lines = Detail.Estimate.Lines.OrderBy(l => l.LineNumber).Select(line =>
        {
            var topBreak = line.QuantityBreaks.OrderByDescending(q => q.Quantity).FirstOrDefault();
            return new ConvertLinePageInput
            {
                EstimateLineId = line.Id,
                Quantity = topBreak?.Quantity ?? 0,
                UnitPrice = topBreak?.UnitPrice ?? 0m
            };
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Detail = await estimateService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var conversion = new EstimateConversionInput(
                Id,
                CustomerPoNumber,
                RequestedShipDate,
                Notes,
                Lines.Select(l => new EstimateConversionLineInput(
                    l.EstimateLineId, l.Quantity, l.UnitPrice, l.LineNotes)).ToList());

            var order = await salesOrderService.CreateFromEstimateAsync(conversion, cancellationToken);
            return RedirectToPage("/SalesOrders/Edit", new { id = order.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
