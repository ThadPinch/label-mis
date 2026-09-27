using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Storage;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Jobs;

/// <summary>One row of the job's file list.</summary>
public record JobFileInfo(
    Guid Id,
    JobFileCategory Category,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Notes,
    string UploadedBy,
    DateTime UploadedAt,
    bool IsArchived,
    /// <summary>This is the file prepress will impose (newest active Print file).</summary>
    bool IsImpositionSource);

/// <summary>
/// Production files kept on a job (customer artwork, approved artwork, print file, embellishment,
/// imposition, other). Every upload is a new row — nothing is overwritten — and wrong files are
/// archived rather than deleted. The newest active <see cref="JobFileCategory.PrintFile"/> is what
/// <see cref="JobImpositionService"/> imposes; when there is none it falls back to the product artwork.
/// </summary>
public class JobFileService(
    LabelsMisDbContext db,
    IFileStorageClient fileStorage,
    ICurrentUserService currentUser)
{
    private const string KeyPrefix = "job-files/";
    private const long MaxBytes = 200L * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".ai", ".eps", ".svg", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".gif", ".psd",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".rtf", ".zip"
    };

    public static string CategoryLabel(JobFileCategory category) => category switch
    {
        JobFileCategory.CustomerArtwork => "Customer artwork",
        JobFileCategory.ApprovedArtwork => "Approved artwork",
        JobFileCategory.PrintFile => "Print file",
        JobFileCategory.Embellishment => "Embellishment",
        JobFileCategory.Imposition => "Imposition",
        JobFileCategory.Other => "Other",
        _ => category.ToString()
    };

    public async Task<IReadOnlyList<JobFileInfo>> ListAsync(
        Guid jobId,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.JobFiles.AsNoTracking()
            .Where(f => f.JobId == jobId && (includeArchived || f.ArchivedAt == null))
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id, f.Category, f.OriginalFileName, f.ContentType, f.FileSizeBytes, f.Notes, f.CreatedAt, f.ArchivedAt,
                UploadedBy = db.Users.Where(u => u.Id == f.CreatedById).Select(u => u.Email ?? u.UserName).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var sourceId = rows
            .Where(r => r.ArchivedAt == null && r.Category == JobFileCategory.PrintFile)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefault();

        return rows.Select(r => new JobFileInfo(
            r.Id,
            r.Category,
            r.OriginalFileName,
            r.ContentType,
            r.FileSizeBytes,
            r.Notes,
            r.UploadedBy ?? "—",
            r.CreatedAt,
            r.ArchivedAt != null,
            r.Id == sourceId)).ToList();
    }

    /// <summary>The newest active Print file on the job, or null — the imposition source when present.</summary>
    public Task<JobFile?> GetPrintFileAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        db.JobFiles.AsNoTracking()
            .Where(f => f.JobId == jobId && f.ArchivedAt == null && f.Category == JobFileCategory.PrintFile)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<JobFile> UploadAsync(
        Guid jobId,
        JobFileCategory category,
        IFormFile file,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file);
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var jobExists = await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, cancellationToken);
        if (!jobExists)
        {
            throw new InvalidOperationException("Job not found.");
        }

        var extension = Path.GetExtension(file.FileName);
        var key = $"{KeyPrefix}{jobId}/{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";

        await using var stream = file.OpenReadStream();
        await fileStorage.UploadAsync(key, stream, file.ContentType, cancellationToken);

        var jobFile = JobFile.Create(
            Guid.NewGuid(),
            jobId,
            category,
            key,
            Path.GetFileName(file.FileName),
            file.ContentType,
            file.Length,
            notes,
            userId,
            now);

        db.JobFiles.Add(jobFile);
        await db.SaveChangesAsync(cancellationToken);
        return jobFile;
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenAsync(
        Guid jobId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await db.JobFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.JobId == jobId, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(file.FileKey, cancellationToken);
        return (stream, ContentTypeFor(file), file.OriginalFileName);
    }

    public async Task ArchiveAsync(Guid jobId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await db.JobFiles.SingleOrDefaultAsync(f => f.Id == fileId && f.JobId == jobId, cancellationToken)
            ?? throw new InvalidOperationException("File not found on this job.");
        file.Archive(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid jobId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await db.JobFiles.SingleOrDefaultAsync(f => f.Id == fileId && f.JobId == jobId, cancellationToken)
            ?? throw new InvalidOperationException("File not found on this job.");
        file.Restore(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecategorizeAsync(Guid jobId, Guid fileId, JobFileCategory category, CancellationToken cancellationToken = default)
    {
        var file = await db.JobFiles.SingleOrDefaultAsync(f => f.Id == fileId && f.JobId == jobId, cancellationToken)
            ?? throw new InvalidOperationException("File not found on this job.");
        file.Recategorize(category, RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Browsers get a real type for previews; stored types from uploads are sometimes blank or generic.</summary>
    private static string ContentTypeFor(JobFile file)
    {
        var byExtension = Path.GetExtension(file.OriginalFileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".tif" or ".tiff" => "image/tiff",
            _ => null
        };
        if (byExtension is not null)
        {
            return byExtension;
        }

        return string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
    }

    private static void ValidateFile(IFormFile file)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("The file is empty.");
        }

        if (file.Length > MaxBytes)
        {
            throw new InvalidOperationException("The file exceeds the 200 MB limit.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File type '{extension}' is not allowed for job files.");
        }
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
