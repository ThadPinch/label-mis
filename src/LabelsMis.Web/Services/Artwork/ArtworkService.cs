using LabelsMis.Domain.Storage;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Artwork;

public class ArtworkService(
    LabelsMisDbContext db,
    IFileStorageClient fileStorage,
    StorageSettingsService storageSettings,
    ICurrentUserService currentUser)
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".ai", ".eps", ".png", ".jpg", ".jpeg", ".tif", ".tiff"
    };

    public async Task UploadForProductAsync(
        Guid productId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file);
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
        var now = DateTime.UtcNow;

        var product = await db.Products.SingleAsync(p => p.Id == productId, cancellationToken);
        var settings = await storageSettings.GetOrCreateAsync(cancellationToken);
        var extension = Path.GetExtension(file.FileName);
        var key = $"{settings.ArtworkKeyPrefix}{productId}/{now:yyyyMMddHHmmss}{extension}";

        // Keys are timestamped and unique per upload, and orders/jobs snapshot the exact key they
        // ran. We intentionally do NOT delete the prior file — old versions must remain resolvable
        // for those snapshots. The product simply points at the newest key. See docs/labelspec-refactor.md.
        await using var stream = file.OpenReadStream();
        await fileStorage.UploadAsync(key, stream, file.ContentType, cancellationToken);

        product.Update(
            product.CustomerSku,
            product.Description,
            product.LabelAcrossIn,
            product.LabelAroundIn,
            product.CornerRadiusIn,
            product.SubstrateId,
            product.InkSet,
            product.FinishingOperationsJson,
            product.DieId,
            key,
            userId,
            now);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenForProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product?.ArtworkFilePath is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(product.ArtworkFilePath, cancellationToken);
        var extension = Path.GetExtension(product.ArtworkFilePath);
        var contentType = extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
        return (stream, contentType, Path.GetFileName(product.ArtworkFilePath));
    }

    private static void ValidateFile(IFormFile file)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Artwork file is empty.");
        }

        if (file.Length > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException("Artwork file exceeds 100 MB limit.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File type '{extension}' is not allowed for artwork.");
        }
    }
}
