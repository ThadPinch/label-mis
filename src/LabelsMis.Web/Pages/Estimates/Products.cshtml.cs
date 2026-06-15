using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Estimates;

[Authorize(Policy = TransactionPolicies.EstimatesEdit)]
public class ProductsModel(ProductService productService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid? customerId, bool house, CancellationToken cancellationToken)
    {
        var items = house
            ? await productService.ListPickerHouseAsync(cancellationToken)
            : !customerId.HasValue || customerId.Value == Guid.Empty
                ? await productService.ListPickerAllAsync(cancellationToken)
                : await productService.ListPickerForCustomerAsync(customerId.Value, cancellationToken);

        return new JsonResult(items);
    }
}
