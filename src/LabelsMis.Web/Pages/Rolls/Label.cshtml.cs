using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Rolls;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;

namespace LabelsMis.Web.Pages.Rolls;

/// <summary>Serves the roll's printable 4" × 6" label as an inline PDF (opens in the tab it was requested from).</summary>
[Authorize(Policy = TransactionPolicies.InventoryRead)]
public class LabelModel(RollService rollService, RollLabelPdfGenerator pdfGenerator) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var label = await rollService.GetLabelAsync(Id, cancellationToken);
        if (label is null) return NotFound();

        var bytes = await pdfGenerator.GenerateBytesAsync(label, cancellationToken);
        var fileName = $"roll-label-{label.Barcode.Replace('/', '-')}.pdf";
        Response.Headers[HeaderNames.ContentDisposition] =
            new ContentDispositionHeaderValue("inline") { FileName = fileName }.ToString();
        return File(bytes, "application/pdf");
    }
}
