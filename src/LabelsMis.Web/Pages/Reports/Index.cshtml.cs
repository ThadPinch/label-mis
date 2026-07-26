using System.Text.Json;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Dashboard;
using LabelsMis.Web.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Reports;

[Authorize(Policy = TransactionPolicies.FinanceReports)]
public class IndexModel(DashboardService dashboardService, ReportService reportService, LabelsMisDbContext db) : PageModel
{
    private static readonly JsonSerializerOptions ChartJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] AllowedRanges = [30, 90, 180, 365];

    [BindProperty(SupportsGet = true)] public int Range { get; set; } = 180;
    [BindProperty(SupportsGet = true)] public ReportType? ReportType { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? ProductId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? DieId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? SupplierId { get; set; }

    public DashboardData Data { get; private set; } = null!;
    public GeneratedReport? Report { get; private set; }

    public string RangeLabel => Range switch
    {
        30 => "last 30 days",
        90 => "last 3 months",
        180 => "last 6 months",
        _ => "last 12 months"
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!AllowedRanges.Contains(Range))
        {
            Range = 180;
        }

        Data = await dashboardService.GetAsync(
            Range,
            includeSales: true,
            includeJobs: false,
            includeEstimates: false,
            includeFinance: true,
            includeInventory: false,
            includeShipping: false,
            cancellationToken);

        await LoadFilterLookupsAsync(cancellationToken);

        if (ReportType is { } type)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            From ??= today.AddDays(-30);
            To ??= today;
            Report = await reportService.GenerateAsync(type, From.Value, To.Value, Filters(), cancellationToken);
        }
    }

    public async Task<IActionResult> OnGetExportAsync(
        ReportType reportType,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var report = await reportService.GenerateAsync(reportType, from, to, Filters(), cancellationToken);
        var fileName = $"{reportType.ToString().ToLowerInvariant()}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv";
        return File(ReportService.ToCsv(report), "text/csv", fileName);
    }

    private ReportFilters Filters() => new(CustomerId, ProductId, DieId, SupplierId);

    private async Task LoadFilterLookupsAsync(CancellationToken cancellationToken)
    {
        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["ProductOptions"] = await db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.InternalSku)
            .Select(p => new SelectListItem($"{p.InternalSku} — {p.Description}", p.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["DieOptions"] = await db.Dies.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Description)
            .Select(d => new SelectListItem(d.Description, d.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["SupplierOptions"] = await db.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(cancellationToken);
    }

    /// <summary>Chart payload for dashboard.js — the finance series moved off the dashboard.</summary>
    public string ChartJson => JsonSerializer.Serialize(new
    {
        rangeLabel = RangeLabel,
        orderIntake = Data.Sales?.OrderIntake,
        revenueTrend = Data.Finance?.RevenueTrend,
        arAging = Data.Finance?.ArAging,
        topCustomers = Data.Finance?.TopCustomers
    }, ChartJsonOptions);
}
