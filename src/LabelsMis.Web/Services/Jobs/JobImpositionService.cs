using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Domain.Estimating.Models;
using LabelsMis.Domain.Storage;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Jobs;

/// <summary>The imposition state of a job as the job page shows it.</summary>
public record JobImpositionView(
    Guid JobId,
    string JobNumber,
    ImpositionTemplate Template,
    /// <summary>The template is a computed default — nothing has been saved on the job yet.</summary>
    bool IsSeeded,
    /// <summary>Where the default came from ("die …", "estimate layout", "spec"), for the hint text.</summary>
    string SeedSource,
    string? ArtworkFilePath,
    string? ArtworkFileName,
    /// <summary>The product artwork is a type the imposer can place (PDF/AI/PNG/JPEG).</summary>
    bool ArtworkCanBeImposed,
    string? ImposedArtworkFilePath,
    DateTime? ImposedAt,
    /// <summary>The imposed PDF was built from an older artwork file than the product now carries.</summary>
    bool ImposedIsStale,
    /// <summary>The imposed PDF was uploaded by hand — the template inputs are inactive.</summary>
    bool ImposedIsManual);

/// <summary>Editable template fields as they arrive from the job page.</summary>
public record ImpositionTemplateInput(
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    decimal GutterAcrossIn,
    decimal GutterAroundIn,
    decimal BleedIn,
    int LabelsAcross,
    int LabelsAround,
    LabelOrientation Orientation,
    decimal WebWidthIn,
    decimal CrossWebOffsetIn,
    ImpositionMarkSide EyeMarks,
    decimal EyeMarkWidthIn,
    decimal EyeMarkHeightIn,
    bool IncludeDieLines,
    bool IncludeSlug)
{
    public ImpositionTemplate ToTemplate() => ImpositionTemplate.Create(
        LabelAcrossIn, LabelAroundIn, CornerRadiusIn, GutterAcrossIn, GutterAroundIn, BleedIn,
        LabelsAcross, LabelsAround, Orientation, WebWidthIn, CrossWebOffsetIn,
        EyeMarks, EyeMarkWidthIn, EyeMarkHeightIn, IncludeDieLines, IncludeSlug);

    public static ImpositionTemplateInput From(ImpositionTemplate t) => new(
        t.LabelAcrossIn, t.LabelAroundIn, t.CornerRadiusIn, t.GutterAcrossIn, t.GutterAroundIn, t.BleedIn,
        t.LabelsAcross, t.LabelsAround, t.Orientation, t.WebWidthIn, t.CrossWebOffsetIn,
        t.EyeMarks, t.EyeMarkWidthIn, t.EyeMarkHeightIn, t.IncludeDieLines, t.IncludeSlug);
}

public record ImpositionRunOutcome(
    ImpositionTemplate Template,
    string ImposedArtworkFilePath,
    IReadOnlyList<string> Warnings,
    ImpositionSourceInfo Source);

/// <summary>
/// Owns a job's imposition: seeds a template from the job's spec (die layout when there is one,
/// else the estimate's computed layout), stores prepress edits, and runs the step-and-repeat to
/// produce the imposed PDF that sits beside the product's original artwork.
/// </summary>
public class JobImpositionService(
    LabelsMisDbContext db,
    IFileStorageClient fileStorage,
    StorageSettingsService storageSettings,
    ICurrentUserService currentUser,
    EstimateCalculationMapper calculationMapper,
    EstimatingService estimatingService,
    ImpositionPdfGenerator generator)
{
    public async Task<JobImpositionView?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(jobId, tracking: false, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var (template, isSeeded, seedSource) = job.Imposition is { } stored
            ? (stored, false, string.Empty)
            : await BuildDefaultAsync(job, cancellationToken);

        return ToView(job, template, isSeeded, seedSource);
    }

    /// <summary>Stores the template on the job without generating anything.</summary>
    public async Task<ImpositionTemplate> SaveTemplateAsync(Guid jobId, ImpositionTemplateInput input, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(jobId, tracking: true, cancellationToken)
            ?? throw new InvalidOperationException("Job not found.");
        var template = input.ToTemplate();
        job.SetImposition(template, RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return template;
    }

    /// <summary>Discards prepress edits: the job goes back to the computed default on next view.</summary>
    public async Task<ImpositionTemplate> ResetTemplateAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(jobId, tracking: true, cancellationToken)
            ?? throw new InvalidOperationException("Job not found.");
        var (template, _, _) = await BuildDefaultAsync(job, cancellationToken);
        job.SetImposition(template, RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return template;
    }

    /// <summary>Saves the template (when supplied), imposes the product's current artwork with it,
    /// stores the PDF next to the artwork and records it on the job.</summary>
    public async Task<ImpositionRunOutcome> RunAsync(Guid jobId, ImpositionTemplateInput? input, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(jobId, tracking: true, cancellationToken)
            ?? throw new InvalidOperationException("Job not found.");
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        ImpositionTemplate template;
        if (input is not null)
        {
            template = input.ToTemplate();
        }
        else if (job.Imposition is { } stored)
        {
            template = stored;
        }
        else
        {
            (template, _, _) = await BuildDefaultAsync(job, cancellationToken);
        }

        var artworkKey = job.Product.ArtworkFilePath;
        if (string.IsNullOrWhiteSpace(artworkKey))
        {
            throw new InvalidOperationException("Upload the product's artwork before running the imposition.");
        }

        var artworkName = job.Product.ArtworkOriginalFileName ?? Path.GetFileName(artworkKey);
        if (!ImpositionPdfGenerator.CanImpose(artworkName) && !ImpositionPdfGenerator.CanImpose(artworkKey))
        {
            throw new InvalidOperationException(
                $"'{Path.GetExtension(artworkName)}' artwork can't be imposed — upload the label as a PDF (or PDF-compatible .ai) or a PNG/JPEG.");
        }

        byte[] artworkBytes;
        await using (var stream = await fileStorage.OpenReadAsync(artworkKey, cancellationToken))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            artworkBytes = buffer.ToArray();
        }

        var slug = new ImpositionSlug(job.JobNumber, job.SalesOrderLine.Description ?? job.Product.Description, now);
        var result = await Task.Run(
            () => generator.Render(artworkBytes, ImpositionPdfGenerator.CanImpose(artworkName) ? artworkName : artworkKey, template, slug),
            cancellationToken);

        var settings = await storageSettings.GetOrCreateAsync(cancellationToken);
        var key = $"{settings.ArtworkKeyPrefix}{job.ProductId}/imposed/{job.JobNumber}-{now:yyyyMMddHHmmss}.pdf";
        await using (var upload = new MemoryStream(result.PdfBytes))
        {
            await fileStorage.UploadAsync(key, upload, "application/pdf", cancellationToken);
        }

        job.SetImposition(template, userId, now);
        job.RecordImposedArtwork(key, artworkKey, userId, now);
        await db.SaveChangesAsync(cancellationToken);

        return new ImpositionRunOutcome(template, key, result.Warnings, result.Source);
    }

    private static readonly HashSet<string> ManualExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    /// <summary>Stores a hand-made imposed PDF on the job, replacing whatever imposition was there.
    /// The template is left intact so a later Run can regenerate over it.</summary>
    public async Task UploadManualAsync(Guid jobId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("The imposition file is empty.");
        }

        if (file.Length > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException("The imposition file exceeds the 100 MB limit.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!ManualExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Upload the imposition as a press-ready PDF.");
        }

        var job = await LoadJobAsync(jobId, tracking: true, cancellationToken)
            ?? throw new InvalidOperationException("Job not found.");
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var settings = await storageSettings.GetOrCreateAsync(cancellationToken);
        var key = $"{settings.ArtworkKeyPrefix}{job.ProductId}/imposed/{job.JobNumber}-manual-{now:yyyyMMddHHmmss}.pdf";
        await using (var stream = file.OpenReadStream())
        {
            await fileStorage.UploadAsync(key, stream, "application/pdf", cancellationToken);
        }

        job.RecordManualImposedArtwork(key, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes the job's imposed PDF (keeping the template): deletes the stored file and
    /// clears the reference. No-op when there is nothing imposed.</summary>
    public async Task DeleteImposedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(jobId, tracking: true, cancellationToken)
            ?? throw new InvalidOperationException("Job not found.");
        if (job.ImposedArtworkFilePath is not { } key)
        {
            return;
        }

        // Best-effort blob delete — the reference is cleared regardless so the job never points at a
        // file that may have already gone.
        try
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
        }
        catch
        {
            // ignore: the object may already be gone; clearing the job reference is what matters.
        }

        job.ClearImposedArtwork(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Opens the job's imposed PDF for preview/download; null when none has been generated.</summary>
    public async Task<(Stream Stream, string FileName)?> OpenImposedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new { j.JobNumber, j.ImposedArtworkFilePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (job?.ImposedArtworkFilePath is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(job.ImposedArtworkFilePath, cancellationToken);
        return (stream, $"{job.JobNumber}-imposed.pdf");
    }

    /// <summary>The template a job starts from: the die's layout when the spec/product names one,
    /// otherwise the estimating engine's best fit for the spec, with the substrate's roll width as the web.</summary>
    public async Task<(ImpositionTemplate Template, bool IsSeeded, string Source)> BuildDefaultAsync(Job job, CancellationToken cancellationToken)
    {
        var spec = job.Spec ?? job.Product.ToLabelSpec();
        var dieId = spec.DieId ?? job.Product.DieId;
        var die = dieId is { } id
            ? await db.Dies.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            : null;

        var press = await db.Presses.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == Press.Indigo6800Id, cancellationToken);
        var stock = spec.SubstrateId != Guid.Empty
            ? await db.Stocks.AsNoTracking().SingleOrDefaultAsync(s => s.Id == spec.SubstrateId, cancellationToken)
            : null;

        var pressWebWidth = press?.WebWidthIn ?? 13.39m;
        var webWidth = stock is { WidthIn: > 0 } && stock.WidthIn <= pressWebWidth ? stock.WidthIn : pressWebWidth;

        // The estimating engine's layout: how many fit across the imageable width and around the max repeat.
        ImpositionResult? computed = null;
        try
        {
            var customerId = job.SalesOrderLine?.SalesOrder?.CustomerId ?? job.Product.PrimaryCustomerId;
            if (customerId is { } cid && spec.SubstrateId != Guid.Empty)
            {
                var input = JobService.ToEstimateLineInput(spec, Math.Max(1, job.QuantityPlanned));
                var request = await calculationMapper.BuildRequestAsync(cid, input, cancellationToken);
                var result = estimatingService.Calculate(request);
                computed = result.Errors.Count == 0 ? result.Imposition : null;
            }
        }
        catch
        {
            // A stale spec (deleted stock, retired ink) shouldn't stop prepress from imposing by hand.
        }

        if (die is not null)
        {
            // The die dictates size, gutters and across; around packs the press's max repeat at the die's pitch.
            var maxRepeat = press is { MaxRepeatIn: > 0 } ? press.MaxRepeatIn : 38.58m;
            var labelsAround = Math.Max(1, (int)Math.Floor(maxRepeat / Math.Max(0.0001m, die.LabelAroundIn + die.GutterAroundIn)));
            var template = ImpositionTemplate.Create(
                die.LabelAcrossIn, die.LabelAroundIn, die.CornerRadiusIn, die.GutterAcrossIn, die.GutterAroundIn, spec.BleedIn,
                die.LabelsAcross, labelsAround, LabelOrientation.AsEntered, webWidth);
            return (template, true, $"die “{die.Description}”");
        }

        if (computed is not null)
        {
            var template = ImpositionTemplate.Create(
                spec.LabelAcrossIn, spec.LabelAroundIn, spec.CornerRadiusIn, spec.GutterAcrossIn, spec.GutterAroundIn, spec.BleedIn,
                computed.LabelsAcross, computed.LabelsAround, computed.Orientation, webWidth);
            return (template, true, "the estimate layout");
        }

        // No die and the engine couldn't run: a one-up frame the operator can build out.
        var fallback = ImpositionTemplate.Create(
            spec.LabelAcrossIn, spec.LabelAroundIn, spec.CornerRadiusIn, spec.GutterAcrossIn, spec.GutterAroundIn, spec.BleedIn,
            Math.Max(1, spec.MaxLabelsAcrossOverride ?? 1), 1, spec.LabelOrientationOverride ?? LabelOrientation.AsEntered, webWidth);
        return (fallback, true, "the job spec");
    }

    private async Task<Job?> LoadJobAsync(Guid jobId, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.Jobs
            .Include(j => j.Product)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder)
            .AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
    }

    private static JobImpositionView ToView(Job job, ImpositionTemplate template, bool isSeeded, string seedSource)
    {
        var artworkKey = job.Product.ArtworkFilePath;
        var artworkName = job.Product.ArtworkOriginalFileName ?? (artworkKey is null ? null : Path.GetFileName(artworkKey));
        return new JobImpositionView(
            job.Id,
            job.JobNumber,
            template,
            isSeeded,
            seedSource,
            artworkKey,
            artworkName,
            artworkKey is not null && (ImpositionPdfGenerator.CanImpose(artworkName) || ImpositionPdfGenerator.CanImpose(artworkKey)),
            job.ImposedArtworkFilePath,
            job.ImposedAt,
            job.ImposedArtworkIsStale(artworkKey),
            job.ImposedIsManual);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
