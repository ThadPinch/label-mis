using System.Text.Json;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Reports;

[Authorize(Policy = TransactionPolicies.FinanceReports)]
public class EvaModel(EvaService evaService, LabelsMisDbContext db) : PageModel
{
    private static readonly JsonSerializerOptions ChartJsonOptions = new(JsonSerializerDefaults.Web);

    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public bool FinishedOnly { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }

    public EvaReport Report { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        From ??= today.AddDays(-90);
        To ??= today;

        Report = await evaService.GetAsync(From.Value, To.Value, CustomerId, FinishedOnly, Sort, cancellationToken);

        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetExportAsync(
        DateOnly from,
        DateOnly to,
        Guid? customerId,
        bool finishedOnly,
        CancellationToken cancellationToken)
    {
        var report = await evaService.GetAsync(from, to, customerId, finishedOnly, null, cancellationToken);
        return File(EvaService.ToCsv(report), "text/csv", $"eva-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    /// <summary>Top jobs by absolute cost variance, for the chart.</summary>
    public string ChartJson => JsonSerializer.Serialize(new
    {
        rangeLabel = $"{From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
        varianceByJob = Report.Rows
            .Where(r => r.CostVariance is not null)
            .OrderByDescending(r => Math.Abs(r.CostVariance!.Value))
            .Take(12)
            .Select(r => new { label = r.JobNumber, value = r.CostVariance!.Value, count = 1 })
            .ToList(),
        marginByJob = Report.Rows
            .Where(r => r.ActualMarginPct is not null)
            .OrderBy(r => r.ActualMarginPct)
            .Take(12)
            .Select(r => new { label = r.JobNumber, value = r.ActualMarginPct!.Value, count = 1 })
            .ToList()
    }, ChartJsonOptions);
}
