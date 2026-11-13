using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Infrastructure.Security;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests.QuickBooks;

/// <summary>Every class that writes the single QuickBooksConnection row sits in this collection,
/// so they run one after another instead of over each other.</summary>
[CollectionDefinition(Name)]
public class QuickBooksConnectionRowCollection
{
    public const string Name = "QuickBooks connection row";
}

/// <summary>The real services over the PostgreSQL test database, with Intuit replaced by fakes and
/// the clock under the test's control. Requires the test database (same convention as the other
/// integration tests here).</summary>
public abstract class QuickBooksTestHost : IAsyncLifetime
{
    protected static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    protected const string RedirectUri = "http://localhost:5000/quickbooks/callback";
    protected const string RealmId = "9341458679244152";

    protected const string PreferencesAllOn = """
        {"Preferences":{
          "AccountingInfoPrefs":{"ClassTrackingPerTxn":false,"ClassTrackingPerTxnLine":true},
          "SalesFormsPrefs":{"CustomTxnNumbers":true,"CustomField":[
            {"CustomField":[{"Name":"SalesFormsPrefs.UseSalesCustom1","Type":"BooleanType","BooleanValue":true}]},
            {"CustomField":[{"Name":"SalesFormsPrefs.SalesCustomName1","Type":"StringType","StringValue":"P.O. Number"}]}]},
          "TaxPrefs":{"UsingSalesTax":true,"PartnerTaxEnabled":true}}}
        """;

    private ServiceProvider _provider = null!;

    protected ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero));
    protected FakeAuthClient Auth { get; } = new();
    protected FakeApiClient Api { get; } = new();
    protected ISecretProtector Protector => _provider.GetRequiredService<ISecretProtector>();
    protected QuickBooksTokenService Tokens => _provider.GetRequiredService<QuickBooksTokenService>();
    protected IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();
    protected DateTime Now => Time.GetUtcNow().UtcDateTime;

    /// <summary>One copy of the app: its own services and its own in-process refresh gate, over
    /// the shared database, clock and fake Intuit. A test builds a second one to stand in for the
    /// old and new containers running side by side during a deploy.</summary>
    protected ServiceProvider BuildApp()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=labels_mis_test;Username=labels_mis;Password=test_password";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LabelsMisDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<ISecretProtector>(new AesGcmSecretProtector(
            Options.Create(new SecretOptions { Key = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()) })));
        services.AddSingleton<IQuickBooksAuthClient>(Auth);
        services.AddSingleton<IQuickBooksApiClient>(Api);
        services.AddSingleton<QuickBooksSyncLogger>();
        services.AddSingleton<QuickBooksTokenService>();
        services.AddScoped<ICurrentUserService>(_ => new StubCurrentUserService(TestUserId));
        services.AddScoped<QuickBooksGateway>();
        services.AddScoped<QuickBooksConnectionService>();
        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        _provider = BuildApp();

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
            if (!await db.Users.AnyAsync(u => u.Id == TestUserId))
            {
                db.Users.Add(new ApplicationUser
                {
                    Id = TestUserId,
                    UserName = "test@labels-mis.local",
                    Email = "test@labels-mis.local",
                    EmailConfirmed = true,
                    NormalizedEmail = "TEST@LABELS-MIS.LOCAL",
                    NormalizedUserName = "TEST@LABELS-MIS.LOCAL"
                });
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException) when (db.Users.AsNoTracking().Any(u => u.Id == TestUserId))
                {
                    // Another test class seeded the same user in the meantime.
                }
            }

            await db.QuickBooksSyncLogs.ExecuteDeleteAsync();
            await db.QuickBooksConnections.ExecuteDeleteAsync();
        });
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    protected async Task InDbAsync(Func<LabelsMisDbContext, Task> action)
    {
        using var scope = _provider.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>());
    }

    /// <summary>Runs <paramref name="action"/> as one web request would: its own scope and services.</summary>
    protected async Task<T> InRequestAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider);
    }

    protected Task<T> WithConnectionServiceAsync<T>(Func<QuickBooksConnectionService, Task<T>> action) =>
        InRequestAsync(services => action(services.GetRequiredService<QuickBooksConnectionService>()));

    protected Task<T> WithGatewayAsync<T>(Func<QuickBooksGateway, Task<T>> action) =>
        InRequestAsync(services => action(services.GetRequiredService<QuickBooksGateway>()));

    protected async Task<QuickBooksConnection?> ReadConnectionAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>()
            .QuickBooksConnections.AsNoTracking().SingleOrDefaultAsync();
    }

    protected async Task<List<QuickBooksSyncLog>> ReadLogAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>()
            .QuickBooksSyncLogs.AsNoTracking().OrderBy(l => l.CreatedAt).ThenBy(l => l.Operation).ToListAsync();
    }

    protected Task SaveKeysAsync() =>
        WithConnectionServiceAsync(s => s.SaveCredentialsAsync(QuickBooksEnvironment.Sandbox, "client-1", "secret-0123456789"));

    /// <summary>Keys saved and a company connected: tokens access-1 / refresh-1 on file.</summary>
    protected async Task ConnectAsync()
    {
        await SaveKeysAsync();
        await WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-1", RealmId, RedirectUri));
    }

    protected sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    protected sealed class StubCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool CanEditMasterData => true;
        public Task<ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ApplicationUser?>(null);
    }

    /// <summary>Issues access-N / refresh-N pairs, N counting up, and remembers what it was asked.</summary>
    protected sealed class FakeAuthClient : IQuickBooksAuthClient
    {
        private readonly object _lock = new();
        private int _issued;

        public List<(string ClientId, string ClientSecret, string Code, string RedirectUri)> Exchanges { get; } = [];
        public List<string> RefreshTokensUsed { get; } = [];
        public List<string> Revoked { get; } = [];

        /// <summary>Failures handed out, in order, before refreshes start working again.</summary>
        public Queue<Exception> RefreshFailures { get; } = new();
        public Exception? ExchangeFailure { get; set; }
        public Exception? RevokeFailure { get; set; }
        public TimeSpan RefreshDelay { get; set; }

        public string BuildAuthorizeUrl(string clientId, string redirectUri, string state) =>
            $"https://intuit.test/authorize?client_id={clientId}&redirect_uri={redirectUri}&state={state}";

        public Task<QuickBooksTokenResponse> ExchangeCodeAsync(
            QuickBooksAppKeys keys, string code, string redirectUri, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Exchanges.Add((keys.ClientId, keys.ClientSecret, code, redirectUri));
            }

            return ExchangeFailure is null ? Task.FromResult(Next()) : Task.FromException<QuickBooksTokenResponse>(ExchangeFailure);
        }

        public async Task<QuickBooksTokenResponse> RefreshAsync(
            QuickBooksAppKeys keys, string refreshToken, CancellationToken cancellationToken = default)
        {
            Exception? failure;
            lock (_lock)
            {
                RefreshTokensUsed.Add(refreshToken);
                RefreshFailures.TryDequeue(out failure);
            }

            if (RefreshDelay > TimeSpan.Zero)
            {
                await Task.Delay(RefreshDelay, cancellationToken);
            }

            return failure is null ? Next() : throw failure;
        }

        public Task RevokeAsync(QuickBooksAppKeys keys, string token, CancellationToken cancellationToken = default)
        {
            if (RevokeFailure is not null)
            {
                return Task.FromException(RevokeFailure);
            }

            lock (_lock)
            {
                Revoked.Add(token);
            }

            return Task.CompletedTask;
        }

        private QuickBooksTokenResponse Next()
        {
            var n = Interlocked.Increment(ref _issued);
            return new QuickBooksTokenResponse($"access-{n}", 3600, $"refresh-{n}", 8_726_400, 157_680_000, $"tid-auth-{n}");
        }
    }

    /// <summary>Answers company and preferences reads with a healthy sandbox company unless a test
    /// queues something else.</summary>
    protected sealed class FakeApiClient : IQuickBooksApiClient
    {
        private readonly object _lock = new();

        public List<(string Resource, string AccessToken)> Reads { get; } = [];

        /// <summary>Every query and write made, in order, for assertions.</summary>
        public List<(string Query, IReadOnlyDictionary<string, string>? Parameters)> Queries { get; } = [];
        public List<(string Resource, string? Body, string? RequestId, IReadOnlyDictionary<string, string>? Parameters)> Writes { get; } = [];
        public List<IReadOnlyDictionary<string, string>?> ReadParameters { get; } = [];

        /// <summary>Failures handed out, in order, before reads start working again.</summary>
        public Queue<Exception> ReadFailures { get; } = new();
        public string Preferences { get; set; } = PreferencesAllOn;

        /// <summary>Scripted answers. Return a response (or throw a <see cref="QuickBooksApiException"/>)
        /// to answer the call; return null to fall through to the defaults (company/preferences for
        /// reads, an empty <c>QueryResponse</c> for queries, <c>{}</c> for writes). Tests in every lane
        /// script through these instead of editing this class.</summary>
        public Func<string, IReadOnlyDictionary<string, string>?, QuickBooksApiResponse?>? OnRead { get; set; }
        public Func<string, IReadOnlyDictionary<string, string>?, QuickBooksApiResponse?>? OnQuery { get; set; }
        public Func<string, string?, string?, IReadOnlyDictionary<string, string>?, QuickBooksApiResponse?>? OnWrite { get; set; }

        public static QuickBooksApiResponse Ok(string body) => new(200, body, "tid-api", 12);

        public Task<QuickBooksApiResponse> ReadAsync(
            QuickBooksCompanyAccess access,
            string resource,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Exception? failure;
            lock (_lock)
            {
                Reads.Add((resource, access.AccessToken));
                ReadParameters.Add(parameters);
                ReadFailures.TryDequeue(out failure);
            }

            if (failure is not null)
            {
                return Task.FromException<QuickBooksApiResponse>(failure);
            }

            try
            {
                if (OnRead?.Invoke(resource, parameters) is { } scripted)
                {
                    return Task.FromResult(scripted);
                }
            }
            catch (Exception ex)
            {
                return Task.FromException<QuickBooksApiResponse>(ex);
            }

            var body = resource.StartsWith("companyinfo/", StringComparison.Ordinal)
                ? """{"CompanyInfo":{"CompanyName":"Sandbox Company US ef89","LegalName":"Sandbox Company US ef89"}}"""
                : Preferences;
            return Task.FromResult(new QuickBooksApiResponse(200, body, "tid-api", 12));
        }

        public Task<QuickBooksApiResponse> QueryAsync(
            QuickBooksCompanyAccess access,
            string query,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Queries.Add((query, parameters));
            }

            try
            {
                return Task.FromResult(OnQuery?.Invoke(query, parameters) ?? Ok("""{"QueryResponse":{}}"""));
            }
            catch (Exception ex)
            {
                return Task.FromException<QuickBooksApiResponse>(ex);
            }
        }

        public Task<QuickBooksApiResponse> WriteAsync(
            QuickBooksCompanyAccess access,
            string resource,
            string? jsonBody,
            string? requestId = null,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Writes.Add((resource, jsonBody, requestId, parameters));
            }

            try
            {
                return Task.FromResult(OnWrite?.Invoke(resource, jsonBody, requestId, parameters) ?? Ok("{}"));
            }
            catch (Exception ex)
            {
                return Task.FromException<QuickBooksApiResponse>(ex);
            }
        }
    }

    protected static QuickBooksAuthException InvalidGrant() =>
        QuickBooksAuthException.FromResponse(400, """{"error":"invalid_grant","error_description":"Incorrect Token type or clientID"}""", "tid-refused");

    protected static QuickBooksApiException Unauthorized() =>
        QuickBooksApiException.FromResponse(401, """{"fault":{"error":[{"message":"AuthenticationFailed","detail":"Token expired","code":"3200"}]}}""", "tid-401");
}
