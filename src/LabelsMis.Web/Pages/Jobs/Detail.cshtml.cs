using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Production;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Rolls;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Jobs;

[Authorize(Policy = TransactionPolicies.JobsRead)]
public class DetailModel(JobService jobService, ArtworkService artworkService, RollService rollService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public ScheduleJobInput ScheduleInput { get; set; } = new(DateOnly.FromDateTime(DateTime.UtcNow), null);
    [BindProperty] public JobStatus StatusInput { get; set; }
    [BindProperty] public Guid OperationId { get; set; }
    [BindProperty] public int GoodCount { get; set; }
    [BindProperty] public int WasteCount { get; set; }
    [BindProperty] public decimal DowntimeMinutes { get; set; }
    [BindProperty] public DowntimeReasonCode? DowntimeReason { get; set; }
    [BindProperty] public decimal ActualMinutes { get; set; }
    [BindProperty] public decimal? ConsumedLf { get; set; }
    [BindProperty] public string? RollBarcode { get; set; }
    [BindProperty] public string? OrderNotes { get; set; }
    [BindProperty] public IFormFile? ArtworkFile { get; set; }

    public JobDetail? Detail { get; private set; }
    public JobTicketDetail? TicketDetail { get; private set; }
    public IReadOnlyList<OrderJobLink> OrderJobs { get; private set; } = [];
    public OperatorJobView? OperatorView { get; private set; }

    /// <summary>The die assigned by the job's spec (falling back to the product), for the hover details.</summary>
    public Domain.Entities.Die? DieInfo { get; private set; }

    /// <summary>Spot inks from the job's spec with their hits/coverage, for the ink hover details.</summary>
    public IReadOnlyList<(Domain.Entities.Ink Ink, int Hits, decimal CoveragePct)> SpotInkInfo { get; private set; } = [];
    public bool CanEdit { get; private set; }
    public bool CanOperate { get; private set; }
    public bool CanChangeStatus { get; private set; }
    public bool CanShip { get; private set; }
    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<FinishingTaskView> FinishingTasks { get; private set; } = [];

    /// <summary>Consumable rolls for the material-usage pickers (loaded only when a picker is shown).</summary>
    public IReadOnlyList<RollPickerOption> RollOptions { get; private set; } = [];

    /// <summary>The substrate the job runs on (spec, else product default) — heads the roll picker.</summary>
    public Guid SubstrateStockId { get; private set; }
    public bool CanAdvanceStage => CanOperate || CanChangeStatus;
    public bool CanEditOrderNotes => CanOperate || CanChangeStatus;
    public bool CanRecordCounts => CanOperate || CanChangeStatus;

    /// <summary>An operation row in the counts recorder, prefilled with the recorded values or
    /// the expected defaults when nothing has been recorded yet.</summary>
    public record OperationCountsView(
        Guid Id,
        string Label,
        JobOperationStatus Status,
        int PrefillGood,
        int PrefillWaste,
        decimal PrefillDowntime,
        DowntimeReasonCode? Reason,
        decimal PlannedMinutes,
        decimal PrefillActualMinutes,
        bool HasRecorded);

    public IReadOnlyList<OperationCountsView> CountOperations { get; private set; } = [];
    public Guid? DefaultCountsOperationId { get; private set; }
    public (JobStatus Next, string Label)? NextStep =>
        Detail is null ? null : ProductionStages.NextStep(Detail.Job.Status);

    private static readonly string[] PdfExtensions = [".pdf"];
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    private string ArtworkExtension =>
        Path.GetExtension(Detail?.ArtworkFilePath ?? string.Empty).ToLowerInvariant();

    public bool ArtworkIsPdf => PdfExtensions.Contains(ArtworkExtension);
    public bool ArtworkIsImage => ImageExtensions.Contains(ArtworkExtension);
    public bool HasArtwork => !string.IsNullOrWhiteSpace(Detail?.ArtworkFilePath);
    public string? ArtworkFileName => !HasArtwork
        ? null
        : Detail!.ArtworkOriginalFileName ?? Path.GetFileName(Detail.ArtworkFilePath!);

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

    public async Task<IActionResult> OnPostCompleteFinishingTaskAsync(
        Guid operationId, decimal actualMinutes, CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            await jobService.CompleteFinishingTaskAsync(operationId, actualMinutes, cancellationToken: cancellationToken);
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
        CanShip = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Shipping);
        Detail = await jobService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return;

        TicketDetail = await jobService.GetTicketDetailAsync(Id, cancellationToken);
        OrderJobs = await jobService.GetOrderJobsAsync(Id, cancellationToken);
        await LoadEntityInfoAsync(cancellationToken);

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

        var materialByOperation = JobService.MaterialStockByFinishingOperation(Detail.Job.Spec);
        FinishingTasks = Detail.Operations
            .Where(o => o.Operation.OperationType == JobOperationType.Finishing)
            .Select(o => new FinishingTaskView(
                o.Operation.Id,
                o.TypeLabel,
                o.Operation.Status,
                o.Operation.PlannedMinutes,
                o.Operation.ActualMinutes,
                o.Operation.EquipmentId is Guid eid && laminationOpIds.Contains(eid),
                o.Operation.EquipmentId is Guid mid && materialByOperation.TryGetValue(mid, out var stockId) ? stockId : null))
            .ToList();

        SubstrateStockId = Detail.Job.Spec?.SubstrateId is { } specSubstrateId && specSubstrateId != Guid.Empty
            ? specSubstrateId
            : Detail.Substrate.Id;
        var showsRollPicker = CanOperate
            && (Detail.Job.Status == JobStatus.Queued
                || (Detail.Job.Status == JobStatus.Printed && FinishingTasks.Any(t => t.IsLamination && !t.IsDone)));
        if (showsRollPicker)
        {
            RollOptions = await rollService.ListPickerOptionsAsync(cancellationToken);
        }

        if (CanOperate)
        {
            OperatorView = await jobService.GetOperatorViewAsync(Id, cancellationToken);
        }

        BuildCountOperations();

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

    /// <summary>Loads the die and spot-ink entities referenced by the job's spec for the hover popovers.</summary>
    private async Task LoadEntityInfoAsync(CancellationToken cancellationToken)
    {
        if (Detail is null)
        {
            return;
        }

        var spec = Detail.Job.Spec;
        var dieId = spec?.DieId ?? Detail.Job.Product.DieId;
        if (dieId is { } id)
        {
            DieInfo = await db.Dies.AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.Supplier)
                .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        }

        if (spec is not null)
        {
            var spots = Services.Estimates.EstimateCalculationMapper.DeserializeSpots(spec.SpotsJson)
                .OrderBy(s => s.SortOrder)
                .ToList();
            if (spots.Count > 0)
            {
                var inkIds = spots.Select(s => s.InkId).Distinct().ToList();
                var inks = await db.Inks.AsNoTracking()
                    .Where(i => inkIds.Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id, cancellationToken);
                SpotInkInfo = spots
                    .Where(s => inks.ContainsKey(s.InkId))
                    .Select(s => (inks[s.InkId], s.Hits, s.CoveragePct))
                    .ToList();
            }
        }
    }

    /// <summary>Builds the counts-recorder rows: every operation, prefilled with recorded values
    /// or the expected defaults (planned quantity / spec waste), with the default selection
    /// following the job's current stage so returning to a stage re-offers its operation.</summary>
    private void BuildCountOperations()
    {
        if (Detail is null)
        {
            return;
        }

        var job = Detail.Job;
        var expectedGood = job.QuantityPlanned;
        var runningWastePct = job.Spec?.RunningWastePct ?? 0m;
        var expectedWaste = (int)Math.Ceiling(job.QuantityPlanned * runningWastePct);

        CountOperations = Detail.Operations.OrderBy(o => o.Operation.Sequence).Select(o =>
        {
            var operation = o.Operation;
            var hasRecorded = operation.GoodCount > 0 || operation.WasteCount > 0 || operation.DowntimeMinutes > 0;

            // Time prefills from what's been recorded (stated minutes, then clocked time), and
            // otherwise from the plan — so an untouched entry records "took as long as planned"
            // and typing a smaller number reports the step ran faster.
            var clockedMinutes = Math.Round(operation.TimeEntries
                .Where(t => t.ClockedOutAt.HasValue)
                .Sum(t => t.DurationHours) * 60m, 0);
            var prefillActual = operation.ActualMinutes > 0
                ? operation.ActualMinutes
                : clockedMinutes > 0 ? clockedMinutes : operation.PlannedMinutes;

            return new OperationCountsView(
                operation.Id,
                $"{operation.Sequence}. {o.TypeLabel}{(o.EquipmentName is null ? "" : $" — {o.EquipmentName}")}",
                operation.Status,
                hasRecorded ? operation.GoodCount : expectedGood,
                hasRecorded ? operation.WasteCount : expectedWaste,
                operation.DowntimeMinutes,
                operation.DowntimeReasonCode,
                operation.PlannedMinutes,
                prefillActual,
                hasRecorded);
        }).ToList();

        var operations = Detail.Operations.Select(o => o.Operation).OrderBy(o => o.Sequence).ToList();
        var stageOperation = job.Status switch
        {
            JobStatus.Queued => operations.FirstOrDefault(o => o.OperationType == JobOperationType.Press),
            JobStatus.Printed => operations.FirstOrDefault(o => o.OperationType == JobOperationType.Finishing && o.Status != JobOperationStatus.Complete)
                ?? operations.FirstOrDefault(o => o.OperationType == JobOperationType.Finishing),
            JobStatus.Finished or JobStatus.Rewound => operations.FirstOrDefault(o => o.OperationType == JobOperationType.Inspection)
                ?? operations.FirstOrDefault(o => o.OperationType == JobOperationType.Pack),
            JobStatus.Shipped => operations.FirstOrDefault(o => o.OperationType == JobOperationType.Ship),
            _ => null
        };

        DefaultCountsOperationId = stageOperation?.Id
            ?? operations.FirstOrDefault(o => o.Status is JobOperationStatus.Pending or JobOperationStatus.InProgress)?.Id
            ?? operations.FirstOrDefault()?.Id;
    }

    /// <summary>The single "Record &amp; mark …" action: records the stage operation's counts
    /// (when the stage has one), then advances the job — so a stage can't be marked done
    /// without its numbers.</summary>
    public async Task<IActionResult> OnPostRecordAndAdvanceAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            if (OperationId != Guid.Empty)
            {
                await jobService.RecordCountsAsync(
                    OperationId, GoodCount, WasteCount, DowntimeMinutes, DowntimeReason, ActualMinutes, cancellationToken);
            }

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

    private bool CanOperateForUser() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Operator);

    private bool CanChangeStatusForUser() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
}
