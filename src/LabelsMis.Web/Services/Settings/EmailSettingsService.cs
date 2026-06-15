using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Settings;

public class EmailSettingsService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<EmailSettings> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        var userId = currentUser.UserId ?? EmailSettings.SingletonId;
        var now = DateTime.UtcNow;
        settings = EmailSettings.CreateDefault(userId, now);
        db.EmailSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task UpdateAsync(
        EmailSettingsFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
        var now = DateTime.UtcNow;
        var settings = await GetOrCreateAsync(cancellationToken);
        settings.Update(
            input.Enabled,
            input.ApiBaseUrl,
            input.Domain,
            input.ApiKey,
            input.FromName,
            input.FromEmail,
            userId,
            now);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public record EmailSettingsFormInput(
    bool Enabled,
    string ApiBaseUrl,
    string Domain,
    string ApiKey,
    string FromName,
    string FromEmail);
