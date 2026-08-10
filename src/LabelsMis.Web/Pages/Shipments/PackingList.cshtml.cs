using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.SalesOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Shipments;

/// <summary>Downloads the packing-list PDF for a sales order; linked from both shipment tabs.</summary>
[Authorize(Policy = TransactionPolicies.ShippingRead)]
public class PackingListModel(SalesOrderService salesOrderService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid SalesOrderId { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var pdf = await salesOrderService.RenderPackingListPdfAsync(SalesOrderId, cancellationToken);
        return pdf is null
            ? NotFound()
            : File(pdf.Bytes, "application/pdf", $"{pdf.OrderNumber.Replace('/', '-')}-packing-list.pdf");
    }
}
