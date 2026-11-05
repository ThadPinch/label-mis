using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Production;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Rolls;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Jobs;

[Authorize(Policy = TransactionPolicies.JobsRead)]
public class DetailModel(
    JobService jobService,
    ArtworkService artworkService,
    RollService rollService,
    JobImpositionService impositionService,
    JobFileService jobFileService,
    ShipmentService shipmentService,
    LabelsMisDbContext db) : PageModel
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
    /// <summary>Record &amp; advance without the advance: a Rewound job on an order that ships by
    /// Record shipment saves its counts and waits for the shipment.</summary>
    [BindProperty] public bool RecordOnly { get; set; }
    [BindProperty] public string? RollBarcode { get; set; }
    [BindProperty] public string? OrderNotes { get; set; }
    [BindProperty] public string? JobNotes { get; set; }
    [BindProperty] public IFormFile? ArtworkFile { get; set; }
    [BindProperty] public ImpositionForm ImpositionForm { get; set; } = new();
    [BindProperty] public IFormFile? ImpositionFile { get; set; }
    [BindProperty] public IFormFile? JobFileUpload { get; set; }
    [BindProperty] public JobFileCategory JobFileCategory { get; set; } = JobFileCategory.CustomerArtwork;
    [BindProperty] public string? JobFileNotes { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowArchivedFiles { get; set; }

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
    public string? StatusMessage { get; private set; }

    /// <summary>The job's order line against its recorded shipments, and how its method ships.</summary>
    public JobShippingState? Shipping { get; private set; }

    /// <summary>Rewound on an order that ships only by Record shipment: no Mark shipped here.</summary>
    public bool ShipsByRecordOnly =>
        Detail?.Job.Status == JobStatus.Rewound && Shipping?.RequiresShipmentRecord == true;

    /// <summary>Shipped/Closed with nothing recorded on a shipment: the order can't count as shipped.</summary>
    public bool MarkedShippedWithoutShipment =>
        Detail is not null && Shipping?.IsMarkedShippedWithoutShipment(Detail.Job.Status) == true;

    public IReadOnlyList<FinishingTaskView> FinishingTasks { get; private set; } = [];

    /// <summary>Consumable rolls for the material-usage pickers (loaded only when a picker is shown).</summary>
    public IReadOnlyList<RollPickerOption> RollOptions { get; private set; } = [];

    /// <summary>The substrate the job runs on (spec, else product default) — heads the roll picker.</summary>
    public Guid SubstrateStockId { get; private set; }
    /// <summary>The job's imposition template (stored, or the computed default) and imposed-PDF state.</summary>
    public JobImpositionView? Imposition { get; private set; }
    public string? ImpositionMessage { get; private set; }
    public string? ImpositionError { get; private set; }
    public IReadOnlyList<string> ImpositionWarnings { get; private set; } = [];

    /// <summary>Prepress runs the imposition: operators and schedulers (and admins).</summary>
    public bool CanImpose => CanOperate || CanChangeStatus;
    /// <summary>Production files on the job (newest first); archived rows only when asked for.</summary>
    public IReadOnlyList<JobFileInfo> JobFiles { get; private set; } = [];
    public int ArchivedFileCount { get; private set; }
    public string? JobFileMessage { get; private set; }
    /// <summary>Anyone who can work the job can add files; same people can archive their mistakes.</summary>
    public bool CanManageJobFiles => CanOperate || CanChangeStatus;
    public bool CanAdvanceStage => CanOperate || CanChangeStatus;
    public bool CanEditOrderNotes => CanOperate || CanChangeStatus;
    public bool CanEditJobNotes => CanEditOrderNotes;
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
        try
        {
            var shipment = await jobService.SetJobStatusAsync(Id, StatusInput, cancellationToken);
            SetShippedMessage(shipment);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    /// <summary>Puts right a job production marked shipped before a shipment was recorded for it:
    /// on a hand-off method, records the package-less shipment it should have had (the job's status
    /// doesn't change). Methods that need packages go through Record shipment instead.</summary>
    public async Task<IActionResult> OnPostRecordMissingShipmentAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser() && !CanShipForUser()) return Forbid();
        try
        {
            var shipment = await shipmentService.RecordJobShippedAsync(Id, cancellationToken);
            TempData["JobShipMessage"] = shipment is null
                ? "Nothing left to record — the line has already shipped."
                : $"Recorded shipment {shipment.ShipmentNumber} (no packages) for this job.";
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    private void SetShippedMessage(Domain.Entities.Shipment? shipment)
    {
        if (shipment is not null)
        {
            TempData["JobShipMessage"] = $"Marked shipped and recorded on shipment {shipment.ShipmentNumber} (no packages"
                + (shipment.BilledShipping is { } billed ? $", {billed:C} shipping" : string.Empty) + ").";
        }
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
                SetShippedMessage(await jobService.AdvanceJobStatusAsync(Id, step.Next, cancellationToken));
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

    public async Task<IActionResult> OnPostSaveJobNotesAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            await jobService.UpdateJobNotesAsync(Id, JobNotes, cancellationToken);
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

    public async Task<IActionResult> OnPostUploadJobFileAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        if (JobFileUpload is null || JobFileUpload.Length == 0)
        {
            ErrorMessage = "Select a file to add to the job.";
            await LoadPageAsync(cancellationToken);
            return Page();
        }

        try
        {
            var file = await jobFileService.UploadAsync(Id, JobFileCategory, JobFileUpload, JobFileNotes, cancellationToken);
            TempData["JobFileMessage"] = $"Added {file.OriginalFileName} as {JobFileService.CategoryLabel(file.Category)}.";
            return RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = Id }, fragment: "files");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostArchiveJobFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        await jobFileService.ArchiveAsync(Id, fileId, cancellationToken);
        TempData["JobFileMessage"] = "File archived. It stays in the job's history and can be restored.";
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = Id }, fragment: "files");
    }

    public async Task<IActionResult> OnPostRestoreJobFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        await jobFileService.RestoreAsync(Id, fileId, cancellationToken);
        TempData["JobFileMessage"] = "File restored.";
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = Id, showArchivedFiles = true }, fragment: "files");
    }

    public async Task<IActionResult> OnPostRecategorizeJobFileAsync(Guid fileId, JobFileCategory category, CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        await jobFileService.RecategorizeAsync(Id, fileId, category, cancellationToken);
        TempData["JobFileMessage"] = $"File marked as {JobFileService.CategoryLabel(category)}.";
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = Id }, fragment: "files");
    }

    public async Task<IActionResult> OnPostSaveImpositionAsync(CancellationToken cancellationToken) =>
        await RunImpositionAction(async () =>
        {
            await impositionService.SaveTemplateAsync(Id, ImpositionForm.ToInput(), cancellationToken);
            TempData["ImpositionMessage"] = "Imposition template saved on this job.";
        }, cancellationToken);

    public async Task<IActionResult> OnPostRunImpositionAsync(CancellationToken cancellationToken) =>
        await RunImpositionAction(async () =>
        {
            var outcome = await impositionService.RunAsync(Id, ImpositionForm.ToInput(), cancellationToken);
            var t = outcome.Template;
            TempData["ImpositionMessage"] =
                $"Imposed {t.LabelsAcross} × {t.LabelsAround} ({t.LabelsPerFrame} per frame) on a {t.WebWidthIn:0.###}\" × {t.RepeatLengthIn:0.###}\" frame"
                + (outcome.Source.RotatedToFit ? " — artwork rotated 90° to fit the label." : ".");
            TempData["ImpositionWarnings"] = string.Join("\n", outcome.Warnings);
        }, cancellationToken);

    public async Task<IActionResult> OnPostResetImpositionAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            await impositionService.ResetTemplateAsync(Id, cancellationToken);
            TempData["ImpositionMessage"] = "Imposition template reset to the job's default layout.";
            return RedirectToImposition();
        }
        catch (Exception ex)
        {
            ImpositionError = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    /// <summary>Regenerates the imposition from the job's saved template — used to overwrite a manual
    /// upload, so it ignores the (disabled) template form and runs from what's stored.</summary>
    public async Task<IActionResult> OnPostRegenerateImpositionAsync(CancellationToken cancellationToken) =>
        await RunImpositionAction(async () =>
        {
            var outcome = await impositionService.RunAsync(Id, input: null, cancellationToken);
            var t = outcome.Template;
            TempData["ImpositionMessage"] =
                $"Imposed {t.LabelsAcross} × {t.LabelsAround} ({t.LabelsPerFrame} per frame) from the saved template, replacing the uploaded file.";
            TempData["ImpositionWarnings"] = string.Join("\n", outcome.Warnings);
        }, cancellationToken, validateForm: false);

    public async Task<IActionResult> OnPostDeleteImpositionAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        try
        {
            await impositionService.DeleteImposedAsync(Id, cancellationToken);
            TempData["ImpositionMessage"] = "Imposition file deleted. The template is kept — run or upload to make a new one.";
            return RedirectToImposition();
        }
        catch (Exception ex)
        {
            ImpositionError = ex.Message;
            await LoadPageAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadImpositionAsync(CancellationToken cancellationToken)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        if (ImpositionFile is null || ImpositionFile.Length == 0)
        {
            ImpositionError = "Select an imposition PDF to upload.";
            await LoadPageAsync(cancellationToken, keepImpositionForm: true);
            return Page();
        }

        try
        {
            await impositionService.UploadManualAsync(Id, ImpositionFile, cancellationToken);
            TempData["ImpositionMessage"] = "Imposition uploaded — the template inputs are inactive until you re-run it.";
            return RedirectToImposition();
        }
        catch (Exception ex)
        {
            ImpositionError = ex.Message;
            await LoadPageAsync(cancellationToken, keepImpositionForm: true);
            return Page();
        }
    }

    /// <summary>Validates the posted template, runs the action, and re-renders (keeping the operator's
    /// edits) when the template is invalid or the action fails.</summary>
    private async Task<IActionResult> RunImpositionAction(Func<Task> action, CancellationToken cancellationToken, bool validateForm = true)
    {
        if (!CanOperateForUser() && !CanChangeStatusForUser()) return Forbid();
        var templateErrors = validateForm
            ? ModelState
            .Where(kv => kv.Key.StartsWith(nameof(ImpositionForm) + ".", StringComparison.Ordinal))
            .SelectMany(kv => kv.Value?.Errors.Select(e => e.ErrorMessage) ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList()
            : [];
        if (templateErrors.Count > 0)
        {
            ImpositionError = string.Join(" ", templateErrors);
            await LoadPageAsync(cancellationToken, keepImpositionForm: true);
            return Page();
        }

        try
        {
            await action();
            return RedirectToImposition();
        }
        catch (Exception ex)
        {
            ImpositionError = ex.Message;
            await LoadPageAsync(cancellationToken, keepImpositionForm: true);
            return Page();
        }
    }

    private IActionResult RedirectToImposition() =>
        Redirect(Url.Page("/Jobs/Detail", null, new { id = Id }, null, null, "imposition")!);

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

    private async Task LoadPageAsync(CancellationToken cancellationToken, bool keepImpositionForm = false)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
        CanOperate = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Operator);
        CanChangeStatus = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
        CanShip = CanShipForUser();
        StatusMessage = TempData["JobShipMessage"] as string;
        Detail = await jobService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return;

        Shipping = await shipmentService.GetJobShippingStateAsync(Id, cancellationToken);

        TicketDetail = await jobService.GetTicketDetailAsync(Id, cancellationToken);
        OrderJobs = await jobService.GetOrderJobsAsync(Id, cancellationToken);
        var allFiles = await jobFileService.ListAsync(Id, includeArchived: true, cancellationToken);
        ArchivedFileCount = allFiles.Count(f => f.IsArchived);
        JobFiles = ShowArchivedFiles ? allFiles : allFiles.Where(f => !f.IsArchived).ToList();
        JobFileMessage = TempData["JobFileMessage"] as string;
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
        JobNotes = Detail.Job.Notes;

        Imposition = await impositionService.GetAsync(Id, cancellationToken);
        if (Imposition is not null && !keepImpositionForm)
        {
            ImpositionForm = ImpositionForm.From(Imposition.Template);
        }

        ImpositionMessage ??= TempData["ImpositionMessage"] as string;
        if (TempData["ImpositionWarnings"] is string warnings && !string.IsNullOrWhiteSpace(warnings))
        {
            ImpositionWarnings = warnings.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

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

            if (RecordOnly)
            {
                TempData["JobShipMessage"] = "Counts recorded. The job is marked shipped when its shipment is recorded.";
                return RedirectToPage(new { id = Id });
            }

            var detail = await jobService.GetDetailAsync(Id, cancellationToken);
            if (detail is null) return NotFound();
            if (ProductionStages.NextStep(detail.Job.Status) is { } step)
            {
                SetShippedMessage(await jobService.AdvanceJobStatusAsync(Id, step.Next, cancellationToken));
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

    private bool CanShipForUser() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Shipping);
}
