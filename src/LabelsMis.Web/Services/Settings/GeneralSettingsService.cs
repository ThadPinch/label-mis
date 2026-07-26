using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Settings;

public class GeneralSettingsService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    /// <summary>Read-only fetch that never writes — safe to call from PDF generation.</summary>
    public virtual Task<GeneralSettings?> GetAsync(CancellationToken cancellationToken = default) =>
        db.GeneralSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public async Task<GeneralSettings> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.GeneralSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        var userId = currentUser.UserId ?? GeneralSettings.SingletonId;
        var now = DateTime.UtcNow;
        settings = GeneralSettings.CreateDefault(userId, now);
        db.GeneralSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task UpdateAsync(GeneralSettingsFormInput input, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var settings = await GetOrCreateAsync(cancellationToken);
        settings.Update(
            input.CompanyName,
            input.AddressLine1,
            input.AddressLine2,
            input.City,
            input.State,
            input.Zip,
            input.Phone,
            input.Email,
            input.Website,
            input.TermsText,
            input.TaxRate,
            userId,
            DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetLogoAsync(byte[] logoBytes, string contentType, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var settings = await GetOrCreateAsync(cancellationToken);
        settings.SetLogo(logoBytes, contentType, userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearLogoAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var settings = await GetOrCreateAsync(cancellationToken);
        settings.ClearLogo(userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}

public record GeneralSettingsFormInput(
    string CompanyName,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? Zip,
    string? Phone,
    string? Email,
    string? Website,
    string? TermsText,
    decimal TaxRate);
