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
        var request = new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            CannedACL = Amazon.S3.S3CannedACL.Private
        };
        var response = await client.PutObjectAsync(request, cancellationToken);
        return new StoredFile(key, contentType, response.ContentLength);
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
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<(Amazon.S3.AmazonS3Client Client, string Bucket)> CreateClientAsync(CancellationToken cancellationToken)
    {
        var settings = await db.StorageSettings.AsNoTracking().SingleAsync(cancellationToken);
        if (!settings.IsSpacesConfigured)
        {
            throw new InvalidOperationException("DigitalOcean Spaces is not configured.");
        }

        var config = new Amazon.S3.AmazonS3Config
        {
            ServiceURL = settings.ServiceUrl,
            ForcePathStyle = false
        };
        if (!string.IsNullOrWhiteSpace(settings.Region))
        {
            config.AuthenticationRegion = settings.Region;
        }

        var client = new Amazon.S3.AmazonS3Client(settings.AccessKey, settings.SecretKey, config);
        return (client, settings.BucketName);
    }
}
