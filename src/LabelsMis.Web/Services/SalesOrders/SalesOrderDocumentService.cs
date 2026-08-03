using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Storage;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.SalesOrders;

/// <summary>Supporting documents attached to a sales order (external POs, approvals, etc.).</summary>
public class SalesOrderDocumentService(
    LabelsMisDbContext db,
    IFileStorageClient fileStorage,
    ICurrentUserService currentUser)
{
    private const string KeyPrefix = "sales-order-documents/";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".gif",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".rtf",
        ".eml", ".msg", ".zip"
    };

    public async Task UploadAsync(
        Guid salesOrderId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file);
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
        var now = DateTime.UtcNow;

        var orderExists = await db.SalesOrders.AsNoTracking()
            .AnyAsync(o => o.Id == salesOrderId, cancellationToken);
        if (!orderExists)
        {
            throw new InvalidOperationException("Sales order not found.");
        }

        var extension = Path.GetExtension(file.FileName);
        var key = $"{KeyPrefix}{salesOrderId}/{Guid.NewGuid():N}{extension}";

        await using var stream = file.OpenReadStream();
        await fileStorage.UploadAsync(key, stream, file.ContentType, cancellationToken);

        var document = SalesOrderDocument.Create(
            Guid.NewGuid(),
            salesOrderId,
            key,
            Path.GetFileName(file.FileName),
            file.ContentType,
            file.Length,
            userId,
            now);

        db.SalesOrderDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenAsync(
        Guid salesOrderId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await db.SalesOrderDocuments.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == documentId && d.SalesOrderId == salesOrderId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(document.FileKey, cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(document.ContentType)
            ? "application/octet-stream"
            : document.ContentType;
        return (stream, contentType, document.OriginalFileName);
    }

    public async Task DeleteAsync(
        Guid salesOrderId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await db.SalesOrderDocuments
            .SingleOrDefaultAsync(d => d.Id == documentId && d.SalesOrderId == salesOrderId, cancellationToken);
        if (document is null)
        {
            return;
        }

        db.SalesOrderDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        // Best-effort blob cleanup — the row is the source of truth and is already gone.
        try
        {
            await fileStorage.DeleteAsync(document.FileKey, cancellationToken);
        }
        catch (Exception)
        {
            // Orphaned blobs are harmless; don't fail the delete over storage hiccups.
        }
    }

    private static void ValidateFile(IFormFile file)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Document file is empty.");
        }

        if (file.Length > 50 * 1024 * 1024)
        {
            throw new InvalidOperationException("Document file exceeds 50 MB limit.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File type '{extension}' is not allowed for order documents.");
        }
    }
}
