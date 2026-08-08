using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using LabelsMis.Domain.Storage;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace LabelsMis.Infrastructure.Storage;

public class FileStorageService(
    LabelsMisDbContext db,
    LocalFileStorageClient localClient,
    SpacesFileStorageClient spacesClient) : IFileStorageClient
{
    public async Task<StoredFile> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var client = await ResolveClientAsync(cancellationToken);
        return await client.UploadAsync(key, content, contentType, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var client = await ResolveClientAsync(cancellationToken);
        return await client.OpenReadAsync(key, cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var client = await ResolveClientAsync(cancellationToken);
        await client.DeleteAsync(key, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var client = await ResolveClientAsync(cancellationToken);
        return await client.ExistsAsync(key, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var client = await ResolveClientAsync(cancellationToken);
        return await client.ListAsync(prefix, cancellationToken);
    }

    private async Task<IFileStorageClient> ResolveClientAsync(CancellationToken cancellationToken)
    {
        var settings = await db.StorageSettings.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return settings is not null && settings.IsSpacesConfigured ? spacesClient : localClient;
    }
}

public class LocalFileStorageClient(IHostEnvironment environment) : IFileStorageClient
{
    private string RootPath => Path.Combine(environment.ContentRootPath, "data", "artwork");

    public async Task<StoredFile> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
        return new StoredFile(key, contentType, new FileInfo(path).Length);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Artwork file not found.", key);
        }

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(GetPath(key)));

    public Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var root = GetPath(prefix);
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<StoredObjectInfo>>([]);
        }

        var items = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var key = Path.GetRelativePath(RootPath, path).Replace(Path.DirectorySeparatorChar, '/');
                return new StoredObjectInfo(key, info.LastWriteTimeUtc, info.Length);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<StoredObjectInfo>>(items);
    }

    private string GetPath(string key) =>
        Path.Combine(RootPath, key.Replace('/', Path.DirectorySeparatorChar));
}

public class SpacesFileStorageClient(LabelsMisDbContext db) : IFileStorageClient
{
    public async Task<StoredFile> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await CreateClientAsync(cancellationToken);
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            CannedACL = S3CannedACL.Private
        };

        try
        {
            var response = await client.PutObjectAsync(request, cancellationToken);
            return new StoredFile(key, contentType, response.ContentLength);
        }
        catch (Exception ex)
        {
            throw WrapSpacesException(ex, "upload artwork to");
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await CreateClientAsync(cancellationToken);
        var response = await client.GetObjectAsync(bucket, key, cancellationToken);
        var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await CreateClientAsync(cancellationToken);
        await client.DeleteObjectAsync(bucket, key, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await CreateClientAsync(cancellationToken);
        try
        {
            await client.GetObjectMetadataAsync(bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await CreateClientAsync(cancellationToken);
        var items = new List<StoredObjectInfo>();
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };

        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, cancellationToken);
            items.AddRange(response.S3Objects.Select(o =>
                new StoredObjectInfo(o.Key, o.LastModified.ToUniversalTime(), o.Size)));
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated);

        return items;
    }

    private async Task<(AmazonS3Client Client, string Bucket)> CreateClientAsync(CancellationToken cancellationToken)
    {
        var settings = await db.StorageSettings.AsNoTracking().SingleAsync(cancellationToken);
        if (!settings.IsSpacesConfigured)
        {
            throw new InvalidOperationException("DigitalOcean Spaces is not configured.");
        }

        var serviceUrl = SpacesEndpointNormalizer.NormalizeServiceUrl(settings.ServiceUrl, settings.BucketName);
        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = false,
            AuthenticationRegion = SpacesEndpointNormalizer.SigningRegion
        };

        var client = new AmazonS3Client(settings.AccessKey, settings.SecretKey, config);
        return (client, settings.BucketName);
    }

    private static InvalidOperationException WrapSpacesException(Exception ex, string action)
    {
        var detail = ex.InnerException?.Message ?? ex.Message;
        if (ex.Message.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("The remote certificate is invalid", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException(
                $"Could not connect to DigitalOcean Spaces to {action}. "
                + "Use the regional endpoint only, e.g. https://nyc3.digitaloceanspaces.com (without the bucket name). "
                + $"Details: {detail}",
                ex);
        }

        return new InvalidOperationException($"DigitalOcean Spaces failed to {action}: {ex.Message}", ex);
    }
}
