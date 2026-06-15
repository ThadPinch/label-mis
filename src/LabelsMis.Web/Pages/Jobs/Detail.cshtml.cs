using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Production;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Jobs;

[Authorize(Policy = TransactionPolicies.JobsRead)]
public class DetailModel(JobService jobService, ArtworkService artworkService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public ScheduleJobInput ScheduleInput { get; set; } = new(DateOnly.FromDateTime(DateTime.UtcNow), null);
    [BindProperty] public JobStatus StatusInput { get; set; }
    [BindProperty] public int GoodCount { get; set; }
    [BindProperty] public int WasteCount { get; set; }
    [BindProperty] public decimal DowntimeMinutes { get; set; }
    [BindProperty] public DowntimeReasonCode? DowntimeReason { get; set; }
    [BindProperty] public decimal? ConsumedLf { get; set; }
    [BindProperty] public string? RollBarcode { get; set; }
    [BindProperty] public string? OrderNotes { get; set; }
    [BindProperty] public IFormFile? ArtworkFile { get; set; }

    public JobDetail? Detail { get; private set; }
    public OperatorJobView? OperatorView { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanOperate { get; private set; }
    public bool CanChangeStatus { get; private set; }
    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<FinishingTaskView> FinishingTasks { get; private set; } = [];
    public bool CanAdvanceStage => CanOperate || CanChangeStatus;
    public bool CanEditOrderNotes => CanOperate || CanChangeStatus;
    public (JobStatus Next, string Label)? NextStep =>
        Detail is null ? null : ProductionStages.NextStep(Detail.Job.Status);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken);
        return Detail is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostScheduleAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Scheduler)) return Forbid();
        try
        {
            await jobService.ScheduleAsync(Id, ScheduleInput, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSetStatusAsync(CancellationToken cancellationToken)
    {
        if (!CanChangeStatusForUser()) return Forbid();
        await jobService.SetJobStatusAsync(Id, StatusInput, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostClockOnAsync(CancellationToken cancellationToken) =>
        await RunOperatorAction(async () =>
        {
            if (OperatorView?.CurrentOperation is null) throw new InvalidOperationException("No active operation.");
            await jobService.ClockOnAsync(OperatorView.CurrentOperation.Id, cancellationToken);
        }, cancellationToken);

    public async Task<IActionResult> OnPostClockOffAsync(CancellationToken cancellationToken) =>
        await RunOperatorAction(async () =>
        {
            if (OperatorView?.CurrentOperation is null) throw new InvalidOperationException("No active operation.");
            await jobService.ClockOffAsync(
                OperatorView.CurrentOperation.Id, GoodCount, WasteCount, DowntimeMinutes, DowntimeReason, ConsumedLf, cancellationToken);
        }, cancellationToken);

    public async Task<IActionResult> OnPostScanRollAsync(CancellationToken cancellationToken) =>
        await RunOperatorAction(async () =>
        {
            if (OperatorView?.CurrentOperation is null) throw new InvalidOperationException("No active operation.");
            if (string.IsNullOrWhiteSpace(RollBarcode)) throw new InvalidOperationException("Scan or enter a roll barcode.");
            await jobService.ScanRollAsync(OperatorView.CurrentOperation.Id, RollBarcode, cancellationToken);
        }, cancellationToken);

    public async Task<IActionResult> OnPostCompleteOperationAsync(CancellationToken cancellationToken) =>
        await RunOperatorAction(async () =>
        {
            if (OperatorView?.CurrentOperation is null) throw new InvalidOperationException("No active operation.");
            await jobService.CompleteOperationAsync(OperatorView.CurrentOperation.Id, cancellationToken);
        }, cancellationToken);

    public async Task<IActionResult> OnPostAdvanceStageAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            var detail = await jobService.GetDetailAsync(Id, cancellationToken);
            if (detail is null) return NotFound();
            if (ProductionStages.NextStep(detail.Job.Status) is { } step)
            {
                await jobService.AdvanceJobStatusAsync(Id, step.Next, cancellationToken);
            }
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSaveOrderNotesAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            await jobService.UpdateOrderNotesAsync(Id, OrderNotes, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRecordRollUsageAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            await jobService.RecordRollUsageAsync(Id, RollBarcode ?? string.Empty, ConsumedLf ?? 0m, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCompleteFinishingTaskAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            await jobService.CompleteFinishingTaskAsync(operationId, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadArtworkAsync(CancellationToken cancellationToken)
    {
        if (ArtworkFile is null || ArtworkFile.Length == 0)
        {
            ErrorMessage = "Select a file to upload.";
            await LoadPageAsync(cancellationToken);
            return Page();
        }

        var detail = await jobService.GetDetailAsync(Id, cancellationToken);
        if (detail is null) return NotFound();

        try
        {
            await artworkService.UploadForProductAsync(detail.Job.ProductId, ArtworkFile, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    private async Task<IActionResult> RunOperatorAction(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!CanOperateForUser()) return Forbid();
        try
        {
            await LoadPageAsync(cancellationToken);
            if (Detail is null) return NotFound();
            await action();
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
        CanOperate = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Operator);
        CanChangeStatus = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
        Detail = await jobService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return;

        var finishingEquipmentIds = Detail.Operations
            .Where(o => o.Operation.OperationType == JobOperationType.Finishing && o.Operation.EquipmentId.HasValue)
            .Select(o => o.Operation.EquipmentId!.Value)
            .Distinct()
            .ToList();
        var laminationOpIds = finishingEquipmentIds.Count == 0
            ? new HashSet<Guid>()
            : (await db.FinishingOperations.AsNoTracking()
                .Where(f => finishingEquipmentIds.Contains(f.Id) && f.OperationType == FinishingOperationType.Laminate)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken)).ToHashSet();

        FinishingTasks = Detail.Operations
            .Where(o => o.Operation.OperationType == JobOperationType.Finishing)
            .Select(o => new FinishingTaskView(
                o.Operation.Id,
                o.TypeLabel,
                o.Operation.Status,
                o.Operation.EquipmentId is Guid eid && laminationOpIds.Contains(eid)))
            .ToList();

        if (CanOperate)
        {
            OperatorView = await jobService.GetOperatorViewAsync(Id, cancellationToken);
        }

        if (Detail.Job.ScheduledForDate.HasValue)
        {
            ScheduleInput = new ScheduleJobInput(Detail.Job.ScheduledForDate.Value, Detail.Job.ScheduledPressId);
        }

        StatusInput = Detail.Job.Status;
        OrderNotes = Detail.OrderNotes;

        ViewData["PressOptions"] = await db.Presses.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToListAsync(cancellationToken);
    }

    private bool CanOperateForUser() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Operator);

    private bool CanChangeStatusForUser() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
}
