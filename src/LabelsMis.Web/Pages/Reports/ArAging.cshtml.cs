using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Reports;

[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class ArAgingModel(InvoiceService invoiceService) : PageModel
{
    public ArAgingReport? Report { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Report = await invoiceService.GetArAgingAsync(cancellationToken);

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        Report = await invoiceService.GetArAgingAsync(cancellationToken);
        var lines = new List<string> { "Customer,Current,1-30,31-60,61-90,90+,Total" };
        foreach (var row in Report.Rows)
        {
            lines.Add($"{row.CustomerName},{row.Current},{row.Days1To30},{row.Days31To60},{row.Days61To90},{row.Over90},{row.Total}");
        }

        return File(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines)), "text/csv", $"ar-aging-{Report.AsOfDate:yyyyMMdd}.csv");
    }
}
