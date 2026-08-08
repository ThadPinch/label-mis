using LabelsMis.Domain.Storage;

namespace LabelsMis.Web.Services.Pdfs;

/// <summary>
/// Stores generated document PDFs (estimates, invoices, purchase orders) under a tmp/ prefix
/// in file storage — Spaces when configured, the local fallback otherwise. Everything under
/// the prefix is disposable: consumers regenerate on demand and <see cref="Background.TempPdfPurgeService"/>
/// deletes objects past the retention window.
/// </summary>
public class TempPdfStorage(IFileStorageClient fileStorage)
{
    public const string KeyPrefix = "tmp/pdfs/";

    public static bool IsTempKey(string? pathOrKey) =>
        !string.IsNullOrWhiteSpace(pathOrKey) && pathOrKey.StartsWith(KeyPrefix, StringComparison.Ordinal);

    /// <summary>Uploads the PDF and returns its storage key.</summary>
    public async Task<string> SaveAsync(
        string category,
        string fileName,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        var key = $"{KeyPrefix}{category}/{fileName}";
        using var stream = new MemoryStream(bytes, writable: false);
        await fileStorage.UploadAsync(key, stream, "application/pdf", cancellationToken);
        return key;
    }

    /// <summary>True when the stored PDF still exists. Legacy filesystem paths (pre-Spaces rows)
    /// are checked on disk so old records fall through to regeneration cleanly.</summary>
    public Task<bool> ExistsAsync(string pathOrKey, CancellationToken cancellationToken = default) =>
        IsTempKey(pathOrKey)
            ? fileStorage.ExistsAsync(pathOrKey, cancellationToken)
            : Task.FromResult(File.Exists(pathOrKey));

    public async Task DeleteAsync(string pathOrKey, CancellationToken cancellationToken = default)
    {
        if (IsTempKey(pathOrKey))
        {
            await fileStorage.DeleteAsync(pathOrKey, cancellationToken);
        }
        else if (File.Exists(pathOrKey))
        {
            File.Delete(pathOrKey);
        }
    }
}
