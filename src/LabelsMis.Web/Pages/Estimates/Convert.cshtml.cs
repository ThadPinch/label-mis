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

    [Range(1, int.MaxValue, ErrorMessage = "Order quantity must be at least 1 — pick a quantity tier.")]
    public int Quantity { get; set; } = 0;

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    public string? LineNotes { get; set; }

    /// <summary>The quantity tier the user clicked; set by the tier picker. Required so a
    /// conversion is always a conscious choice, not the pre-filled top break.</summary>
    public int? SelectedTierQuantity { get; set; }
}

/// <summary>A quoted non-label charge (die creation, design time) carried onto the order.</summary>
public class ConvertChargePageInput
{
    public Guid? EstimateChargeId { get; set; }

    /// <summary>Unchecked charges are left off the order.</summary>
    public bool Include { get; set; } = true;

    public string? Description { get; set; }

    [Range(1, 1000000)]
    public int Quantity { get; set; } = 1;

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }
}

[Authorize(Policy = TransactionPolicies.SalesOrdersEdit)]
public class ConvertModel(
    EstimateService estimateService,
    SalesOrderService salesOrderService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Customer PO # is required.")]
    public string? CustomerPoNumber { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Requested ship date is required.")]
    public DateOnly? RequestedShipDate { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    [BindProperty]
    public string? BillingNotes { get; set; }

    [BindProperty]
    public List<ConvertLinePageInput> Lines { get; set; } = [];

    [BindProperty]
    public List<ConvertChargePageInput> Charges { get; set; } = [];

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

        // Seed the form with the estimate's notes so they survive conversion unless deliberately
        // edited. Quantity and price stay empty: the user must pick a tier explicitly.
        Notes = Detail.Estimate.Notes;
        BillingNotes = Detail.Estimate.BillingNotes;
        Lines = Detail.Estimate.Lines.OrderBy(l => l.LineNumber).Select(line => new ConvertLinePageInput
        {
            EstimateLineId = line.Id,
            LineNotes = line.LineNotes
        }).ToList();
        Charges = Detail.Estimate.Charges.OrderBy(c => c.LineNumber).Select(charge => new ConvertChargePageInput
        {
            EstimateChargeId = charge.Id,
            Description = charge.Description,
            Quantity = charge.Quantity,
            UnitPrice = charge.UnitPrice
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Detail = await estimateService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();

        var estimateLinesById = Detail.Estimate.Lines.ToDictionary(l => l.Id);
        for (var i = 0; i < Lines.Count; i++)
        {
            // Only require a tier when the line actually has tiers to pick from.
            if (Lines[i].SelectedTierQuantity is null
                && estimateLinesById.TryGetValue(Lines[i].EstimateLineId, out var estimateLine)
                && estimateLine.QuantityBreaks.Count > 0)
            {
                ModelState.AddModelError(
                    $"Lines[{i}].SelectedTierQuantity",
                    $"Pick a quantity tier for line {i + 1}.");
            }
        }

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
                    l.EstimateLineId, l.Quantity, l.UnitPrice, l.LineNotes)).ToList(),
                BillingNotes,
                Charges
                    .Where(c => c.Include && !string.IsNullOrWhiteSpace(c.Description))
                    .Select(c => new EstimateConversionChargeInput(
                        c.EstimateChargeId, c.Description!, c.Quantity, c.UnitPrice)).ToList());

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
