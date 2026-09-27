using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

/// <summary>
/// A production file kept on a job — customer artwork as received, the approved version, the
/// print-ready file, embellishment/foil layers, a hand-made imposition, or anything else the
/// job needs. Files are never overwritten: each upload is its own row, and the file itself lives
/// in blob storage under <see cref="FileKey"/>. Incorrect files are archived, not deleted, so the
/// history of what was on the job stays intact.
/// </summary>
public class JobFile : EntityBase
{
    private JobFile()
    {
    }

    public Guid JobId { get; private set; }
    public Job Job { get; private set; } = null!;
    public JobFileCategory Category { get; private set; }
    public string FileKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? ArchivedAt { get; private set; }
    public Guid? ArchivedById { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static JobFile Create(
        Guid id,
        Guid jobId,
        JobFileCategory category,
        string fileKey,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        string? notes,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(fileKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(fileKey));
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("File name is required.", nameof(originalFileName));
        }

        if (fileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        }

        var file = new JobFile
        {
            JobId = jobId,
            Category = category,
            FileKey = fileKey.Trim(),
            OriginalFileName = originalFileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            FileSizeBytes = fileSizeBytes,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        file.SetCreated(id, createdById, createdAt);
        return file;
    }

    public void Recategorize(JobFileCategory category, Guid modifiedById, DateTime modifiedAt)
    {
        Category = category;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Hides the file from the job without deleting it. Idempotent.</summary>
    public void Archive(Guid archivedById, DateTime archivedAt)
    {
        if (IsArchived)
        {
            return;
        }

        ArchivedAt = archivedAt;
        ArchivedById = archivedById;
        SetModified(archivedById, archivedAt);
    }

    public void Restore(Guid modifiedById, DateTime modifiedAt)
    {
        if (!IsArchived)
        {
            return;
        }

        ArchivedAt = null;
        ArchivedById = null;
        SetModified(modifiedById, modifiedAt);
    }
}
