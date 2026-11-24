using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Infrastructure.Security;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Carriers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests.Carriers;

/// <summary>Every class that writes the single CarrierSettings row (or the company address the
/// ship-from is read from) sits in this collection, so they run one after another.</summary>
[CollectionDefinition(Name)]
public class CarrierSettingsRowCollection
{
    public const string Name = "Carrier settings row";
}

/// <summary>The real carrier services over the PostgreSQL test database, with Worldwide Express
/// replaced by <see cref="FakeCarrierClient"/> and the clock under the test's control. Requires the
/// test database (same convention as the other integration tests here). The carrier settings row
/// and the company address are put back as they were found when the class is done.</summary>
public abstract class CarrierTestHost : IAsyncLifetime
{
    protected static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    protected const string ClientId = "test-client-id";
    protected const string ClientSecret = "test-client-secret-9f3c";

    private ServiceProvider _provider = null!;
    private CompanyAddress? _companyBefore;
    private bool _companyCreated;

    protected ManualTime Time { get; } = new(new DateTimeOffset(2026, 11, 24, 17, 0, 0, TimeSpan.Zero));
    protected FakeCarrierClient Carrier { get; } = new();
    protected DateTime Now => Time.GetUtcNow().UtcDateTime;
    protected IServiceProvider Services => _provider;

    /// <summary>Services a subclass needs on top of the carrier ones.</summary>
    protected virtual void AddServices(IServiceCollection services)
    {
    }

    public virtual async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=labels_mis_test;Username=labels_mis;Password=***";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LabelsMisDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<ISecretProtector>(new AesGcmSecretProtector(
            Options.Create(new SecretOptions { Key = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()) })));
        services.AddSingleton<ICarrierClient>(Carrier);
        services.AddSingleton<CarrierLogger>();
        services.AddScoped<ICurrentUserService>(_ => new StubCurrentUser(TestUserId));
        services.AddScoped<CarrierSettingsService>();
        services.AddScoped<CarrierGateway>();
        services.AddScoped<CarrierConnectionTest>();
        AddServices(services);
        _provider = services.BuildServiceProvider();

        try
        {
            await InDbAsync(db => db.Database.MigrateAsync());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Integration tests require PostgreSQL. Start docker compose or set ConnectionStrings__Default.", ex);
        }

        await InDbAsync(async db =>
        {
            // The person at the screen, and the system user background work is filed under (the
            // app seeds that one at startup; the test database is only migrated).
            foreach (var (id, email) in new[]
                     {
                         (TestUserId, "test@labels-mis.local"),
                         (LabelsMis.Domain.Common.TenantConstants.SystemUserId, "system@labels-mis.local")
                     })
            {
                if (await db.Users.AnyAsync(u => u.Id == id))
                {
                    continue;
                }

                db.Users.Add(new ApplicationUser
                {
                    Id = id,
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    NormalizedEmail = email.ToUpperInvariant(),
                    NormalizedUserName = email.ToUpperInvariant()
                });
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException) when (db.Users.AsNoTracking().Any(u => u.Id == id))
                {
                    // Another test class seeded the same user in the meantime.
                    db.ChangeTracker.Clear();
                }
            }

            await db.CarrierLogs.ExecuteDeleteAsync();
            await db.CarrierSettings.ExecuteDeleteAsync();

            var company = await db.GeneralSettings.SingleOrDefaultAsync();
            if (company is null)
            {
                company = GeneralSettings.CreateDefault(TestUserId, Now);
                db.GeneralSettings.Add(company);
                _companyCreated = true;
            }
            else
            {
                _companyBefore = CompanyAddress.Of(company);
            }

            ShopAddress.ApplyTo(company, TestUserId, Now);
            await db.SaveChangesAsync();
        });
    }

    public virtual async Task DisposeAsync()
    {
        await InDbAsync(async db =>
        {
            await db.CarrierLogs.ExecuteDeleteAsync();
            await db.CarrierSettings.ExecuteDeleteAsync();
            var company = await db.GeneralSettings.SingleOrDefaultAsync();
            if (company is not null && _companyCreated)
            {
                db.GeneralSettings.Remove(company);
            }
            else if (company is not null && _companyBefore is { } before)
            {
                before.ApplyTo(company, TestUserId, Now);
            }

            await db.SaveChangesAsync();
        });
        await _provider.DisposeAsync();
    }

    /// <summary>The shop the tests ship from: complete enough for a label.</summary>
    protected static readonly CompanyAddress ShopAddress = new(
        "Hey Lucky Label Co.", "100 W Main St", "Suite 4", "Phoenix", "AZ", "85001", "(602) 555-0100");

    protected async Task InDbAsync(Func<LabelsMisDbContext, Task> action)
    {
        using var scope = _provider.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>());
    }

    protected async Task<T> InDbAsync<T>(Func<LabelsMisDbContext, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>());
    }

    /// <summary>Runs <paramref name="action"/> as one web request would: its own scope and services.</summary>
    protected async Task<T> InRequestAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider);
    }

    protected async Task InRequestAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = _provider.CreateScope();
        await action(scope.ServiceProvider);
    }

    protected Task WithSettingsAsync(Func<CarrierSettingsService, Task> action) =>
        InRequestAsync(services => action(services.GetRequiredService<CarrierSettingsService>()));

    protected Task<T> WithSettingsAsync<T>(Func<CarrierSettingsService, Task<T>> action) =>
        InRequestAsync(services => action(services.GetRequiredService<CarrierSettingsService>()));

    protected Task<T> WithGatewayAsync<T>(Func<CarrierGateway, Task<T>> action) =>
        InRequestAsync(services => action(services.GetRequiredService<CarrierGateway>()));

    /// <summary>Saves keys the way Setup → Shipping carrier does.</summary>
    protected Task SaveKeysAsync(CarrierEnvironment environment = CarrierEnvironment.Staging) =>
        WithSettingsAsync(s => s.SaveCredentialsAsync(environment, ClientId, ClientSecret));

    protected Task<List<CarrierLog>> ReadLogAsync() =>
        InDbAsync(db => db.CarrierLogs.AsNoTracking().OrderBy(l => l.CreatedAt).ThenBy(l => l.DurationMs).ToListAsync());

    protected Task<CarrierSettings?> ReadSettingsAsync() =>
        InDbAsync(db => db.CarrierSettings.AsNoTracking().SingleOrDefaultAsync());

    /// <summary>The company address in Settings → General, which is where a label ships from.</summary>
    protected sealed record CompanyAddress(
        string CompanyName, string? Line1, string? Line2, string? City, string? State, string? Zip, string? Phone)
    {
        public static CompanyAddress Of(GeneralSettings s) =>
            new(s.CompanyName, s.AddressLine1, s.AddressLine2, s.City, s.State, s.Zip, s.Phone);

        /// <summary>This address as the carrier is given it.</summary>
        public CarrierParty ToParty(string? contact = null) =>
            new(CompanyName, contact, Phone, Line1, Line2, City, State, Zip);

        public void ApplyTo(GeneralSettings s, Guid userId, DateTime at) =>
            s.Update(CompanyName, Line1, Line2, City, State, Zip, Phone, s.Email, s.Website, s.TermsText,
                s.TaxRate, s.DefaultMarkupPct, userId, at);
    }

    protected Task SetCompanyAddressAsync(CompanyAddress address) =>
        InDbAsync(async db =>
        {
            var company = await db.GeneralSettings.SingleAsync();
            address.ApplyTo(company, TestUserId, Now);
            await db.SaveChangesAsync();
        });

    protected sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    protected sealed class StubCurrentUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool CanEditMasterData => true;
        public Task<ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ApplicationUser?>(null);
    }
}
