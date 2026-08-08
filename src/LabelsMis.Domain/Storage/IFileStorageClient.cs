namespace LabelsMis.Domain.Storage;

public record StoredFile(string Key, string ContentType, long SizeBytes);

public record StoredObjectInfo(string Key, DateTime LastModifiedUtc, long SizeBytes);

public interface IFileStorageClient
{
    Task<StoredFile> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken cancellationToken = default);
}
