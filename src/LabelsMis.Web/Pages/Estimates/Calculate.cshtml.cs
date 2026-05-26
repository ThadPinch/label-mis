using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Estimates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Estimates;

[Authorize(Policy = TransactionPolicies.EstimatesEdit)]
[IgnoreAntiforgeryToken]
public class CalculateModel(EstimateService estimateService) : PageModel
{
    public async Task<IActionResult> OnPostAsync(
        [FromBody] EstimatePageInput input,
        CancellationToken cancellationToken)
    {
        var result = await estimateService.CalculateAsync(input.ToForm(), cancellationToken);
        return new JsonResult(new
        {
            lines = result.Lines.Select(l => new
            {
                lineIndex = l.LineIndex,
                quantityBreaks = l.QuantityBreaks,
                imposition = l.Imposition,
                impositionLayout = l.ImpositionLayout,
                warnings = l.Warnings,
                errors = l.Errors,
                markupPctUsed = l.MarkupPctUsed
            })
        });
    }
}
