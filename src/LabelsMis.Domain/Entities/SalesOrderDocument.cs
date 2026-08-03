using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

/// <summary>
/// A supporting file attached to a sales order — external POs, approvals, correspondence.
/// The file itself lives in blob storage under <see cref="FileKey"/>.
/// </summary>
public class SalesOrderDocument : EntityBase
{
    private SalesOrderDocument()
    {
    }

    public Guid SalesOrderId { get; private set; }
    public SalesOrder SalesOrder { get; private set; } = null!;
    public string FileKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }

    public static SalesOrderDocument Create(
        Guid id,
        Guid salesOrderId,
        string fileKey,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        Guid createdById,
        DateTime createdAt)
    {
        var document = new SalesOrderDocument
        {
            SalesOrderId = salesOrderId,
            FileKey = fileKey,
            OriginalFileName = originalFileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes = fileSizeBytes
        };
        document.SetCreated(id, createdById, createdAt);
        return document;
    }
}
