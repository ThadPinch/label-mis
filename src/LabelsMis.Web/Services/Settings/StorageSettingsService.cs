using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Storage;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Settings;

public class StorageSettingsService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<StorageSettings> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.StorageSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        var userId = currentUser.UserId ?? StorageSettings.SingletonId;
        var now = DateTime.UtcNow;
        settings = StorageSettings.CreateDefault(userId, now);
        db.StorageSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task UpdateAsync(
        StorageSettingsFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
        var now = DateTime.UtcNow;
        var settings = await GetOrCreateAsync(cancellationToken);
        settings.Update(
            input.ServiceUrl,
            input.BucketName,
            input.AccessKey,
            input.SecretKey,
            input.Region,
            input.ArtworkKeyPrefix,
            input.UseLocalFallback,
            userId,
            now);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public record StorageSettingsFormInput(
    string ServiceUrl,
    string BucketName,
    string AccessKey,
    string SecretKey,
    string? Region,
    string ArtworkKeyPrefix,
    bool UseLocalFallback);
