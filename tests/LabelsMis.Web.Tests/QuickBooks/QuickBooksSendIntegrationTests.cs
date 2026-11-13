using System.Text.Json.Nodes;
using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Domain.Security;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Infrastructure.Security;
using LabelsMis.Web.Background;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Email;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Pdfs;
using LabelsMis.Web.Services.QuickBooks;
using LabelsMis.Web.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests.QuickBooks;

/// <summary>
/// The send path end to end over the test database: <see cref="QuickBooksSendService"/> queues,
/// <see cref="QuickBooksSendWorker"/> claims, <see cref="QuickBooksInvoiceSender"/> talks to a
/// stand-in QuickBooks company (<see cref="FakeCompany"/>) scripted through the shared fake's hooks.
/// </summary>
[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksSendIntegrationTests : QuickBooksTestHost, IAsyncLifetime
{
    private readonly FakeCompany _qb = new();
    private ServiceProvider _app = null!;

    private QuickBooksSendWorker Worker => _app.GetRequiredService<QuickBooksSendWorker>();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        _app = BuildSendApp();
        Api.OnQuery = _qb.Query;
        Api.OnRead = _qb.Read;
        Api.OnWrite = _qb.Write;

        await InDbAsync(async db =>
        {
            await db.QuickBooksCatalogEntries.ExecuteDeleteAsync();
            await db.QuickBooksSettings.ExecuteDeleteAsync();

            // The worker takes the oldest queued invoice in the whole database: leave none behind
            // from earlier tests.
            await db.Invoices.Where(i => i.QbExportStatus != QuickBooksExportStatus.None)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.QbExportStatus, QuickBooksExportStatus.None));
            var now = DateTime.UtcNow;
            var entries = new (QuickBooksCatalogKind Kind, string Id, string Name, bool? Taxable)[]
            {
                (QuickBooksCatalogKind.Item, "1", "Sales", true),
                (QuickBooksCatalogKind.Item, "2", "Shipping", false),
                (QuickBooksCatalogKind.Item, "3", "Design", false),
                (QuickBooksCatalogKind.Item, "4", "Rush", false),
                (QuickBooksCatalogKind.Item, "5", "Die Creation", false),
                (QuickBooksCatalogKind.Class, "21", "Roll Label", null),
                (QuickBooksCatalogKind.Class, "22", "Shrink Sleeves", null),
                (QuickBooksCatalogKind.Class, "23", "Buyout", null),
                (QuickBooksCatalogKind.Term, "31", "Net 30", null)
            };
            foreach (var e in entries)
            {
                db.QuickBooksCatalogEntries.Add(QuickBooksCatalogEntry.Create(Guid.NewGuid(), e.Kind, e.Id, e.Name, null, e.Taxable, TestUserId, now));
            }

            await db.SaveChangesAsync();
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _app.DisposeAsync();
        await DisposeAsync();
    }

    [Fact]
    public async Task Send_CreatesTheInvoiceAndRecordsQuickBooksFigures()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();

        (await QueueAsync(invoice.Id)).Queued.Should().BeTrue();
        (await Worker.RunOnceAsync(default)).Should().BeTrue();

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        saved.QbInvoiceId.Should().Be("200");
        saved.QbSyncToken.Should().Be("0");
        saved.QbExportedAt.Should().NotBeNull();
        saved.QbExportedById.Should().Be(TestUserId);
        saved.QbTax.Should().Be(40m, "the fake company charges 8% on the taxable Sales line only");
        saved.QbTotal.Should().Be(575m);
        saved.QbPayloadHash.Should().HaveLength(64);

        Api.Queries.Should().ContainSingle().Which.Query.Should().Contain($"DocNumber = '{invoice.Number}'");
        Api.Queries[0].Parameters.Should().ContainKey("include");
        var create = Api.Writes.Should().ContainSingle().Subject;
        create.Resource.Should().Be("invoice");
        create.RequestId.Should().HaveLength(50);
        create.Parameters!["include"].Should().Be("enhancedAllCustomFields");
        var body = JsonNode.Parse(create.Body!)!;
        body["DocNumber"]!.GetValue<string>().Should().Be(invoice.Number);
        body["CustomerRef"]!["value"]!.GetValue<string>().Should().Be(invoice.QbCustomerId);
        body["EmailStatus"]!.GetValue<string>().Should().Be("NeedToSend", "send later is the default");
        body["Line"]!.AsArray().Should().HaveCount(2);

        var log = await ReadLogAsync();
        log.Where(l => l.InvoiceId == invoice.Id).Select(l => l.Operation)
            .Should().BeEquivalentTo([QuickBooksInvoiceSender.LookupOperation, QuickBooksInvoiceSender.CreateOperation]);
        log.Where(l => l.InvoiceId == invoice.Id).Should().OnlyContain(l => l.Automatic && l.Succeeded);

        var state = (await StatesAsync(invoice.Id)).Single();
        state.Label.Should().Be("In QuickBooks");
        state.OpenInQuickBooksUrl.Should().Be("https://app.sandbox.qbo.intuit.com/app/invoice?txnId=200");
        (await Worker.RunOnceAsync(default)).Should().BeFalse("nothing is left in line");
    }

    [Fact]
    public async Task Send_FoundByNumberForTheSameCustomer_LinksInsteadOfCreating()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        _qb.Add("77", invoice.Number, invoice.QbCustomerId, 535m);

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbInvoiceId.Should().Be("77");
        saved.QbExportedAt.Should().NotBeNull();
        saved.QbTotal.Should().Be(535m);
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        Api.Writes.Should().BeEmpty("a second copy is never made");
    }

    [Fact]
    public async Task Send_FoundByNumberForAnotherCustomer_StopsForAPerson()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        _qb.Add("78", invoice.Number, "someone-else", 99m);

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.NeedsAttention);
        saved.QbLastErrorCode.Should().Be(QuickBooksErrorMessages.OtherCustomer);
        saved.QbInvoiceId.Should().BeNull();
        Api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_DuplicateNumber6140_LooksUpAgainAndLinks()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        _qb.Add("79", invoice.Number, invoice.QbCustomerId, 575m);
        _qb.HideFromLookups = 1; // another copy of the app created it just after our look-up

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbInvoiceId.Should().Be("79");
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        Api.Writes.Should().ContainSingle("the one create was refused with 6140");
        Api.Queries.Should().HaveCount(2);
        _qb.Count.Should().Be(1);
    }

    [Fact]
    public async Task Send_AfterUnmarkAndEdit_UpdatesTheSameQuickBooksInvoice()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await SendAsync(invoice.Id);

        await WithInvoiceServiceAsync(s => s.UnmarkExportedAsync(invoice.Id));
        await WithInvoiceServiceAsync(s => s.SetShippingOverrideAsync(invoice.Id, 42.5m));
        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbInvoiceId.Should().Be("200");
        saved.QbSyncToken.Should().Be("1");
        saved.QbTotal.Should().Be(582.5m);
        saved.QbExportedAt.Should().NotBeNull();
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        _qb.Count.Should().Be(1, "the edit updates the invoice instead of making a new one");

        var update = Api.Writes.Last();
        var body = JsonNode.Parse(update.Body!)!;
        body["Id"]!.GetValue<string>().Should().Be("200");
        body["SyncToken"]!.GetValue<string>().Should().Be("0");
        body["sparse"]!.GetValue<bool>().Should().BeTrue();
        body["Line"]!.AsArray()[1]!["Amount"]!.GetValue<decimal>().Should().Be(42.5m);
        Api.Reads.Should().Contain(r => r.Resource == "invoice/200");
    }

    [Fact]
    public async Task Send_UpdateHitByAStaleSyncToken_RereadsAndRetriesOnce()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await SendAsync(invoice.Id);
        await WithInvoiceServiceAsync(s => s.UnmarkExportedAsync(invoice.Id));
        _qb.ConcurrentEditsBeforeUpdate = 1; // someone saves it in QuickBooks between our read and our write

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        saved.QbSyncToken.Should().Be("2");
        var updates = Api.Writes.ToList();
        updates.Should().HaveCount(2);
        JsonNode.Parse(updates[1].Body!)!["SyncToken"]!.GetValue<string>().Should().Be("1");
        Api.Reads.Count(r => r.Resource == "invoice/200").Should().Be(2);
    }

    [Fact]
    public async Task Send_StaleTwice_NeedsAttention()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await SendAsync(invoice.Id);
        await WithInvoiceServiceAsync(s => s.UnmarkExportedAsync(invoice.Id));
        _qb.ConcurrentEditsBeforeUpdate = 2;

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.NeedsAttention);
        saved.QbLastErrorCode.Should().Be("5010");
        saved.QbLastError.Should().Contain("changed this invoice in QuickBooks");
    }

    [Fact]
    public async Task Send_Throttled_WaitsLongerEachTime_ThenNeedsAttention()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        for (var i = 0; i < 5; i++)
        {
            _qb.CreateFailures.Enqueue(Fault(429, "429", "Too many requests"));
        }

        await QueueAsync(invoice.Id);
        foreach (var wait in QuickBooksInvoiceSender.RetryDelays)
        {
            (await Worker.RunOnceAsync(default)).Should().BeTrue();
            var deferred = await ReadInvoiceAsync(invoice.Id);
            deferred.QbExportStatus.Should().Be(QuickBooksExportStatus.Queued);
            deferred.QbNextAttemptAt.Should().Be(Now + wait);
            deferred.QbLastErrorCode.Should().Be("429");
            (await Worker.RunOnceAsync(default)).Should().BeFalse("it isn't due yet");
            Time.Advance(wait);
        }

        (await Worker.RunOnceAsync(default)).Should().BeTrue();
        var stopped = await ReadInvoiceAsync(invoice.Id);
        stopped.QbExportStatus.Should().Be(QuickBooksExportStatus.NeedsAttention);
        stopped.QbAttemptCount.Should().Be(5);
        stopped.QbLastError.Should().Contain("slow down");
        Api.Writes.Should().HaveCount(5);
    }

    [Fact]
    public async Task Send_ValidationFault_NeedsAttentionWithTheShopSentence_AndRetryWorks()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        _qb.CreateFailures.Enqueue(Fault(400, "6000", "A business validation error has occurred while processing your request",
            "Business Validation Error: You must select a customer"));

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var state = (await StatesAsync(invoice.Id)).Single();
        state.Status.Should().Be(QuickBooksExportStatus.NeedsAttention);
        state.Label.Should().Be("Needs attention");
        state.LastError.Should().StartWith("QuickBooks refused the invoice (").And.Contain("You must select a customer");
        state.FixUrl.Should().Be($"/invoices/{invoice.Id}/quickbooks");

        await WithSendServiceAsync(s => s.RetryAsync(invoice.Id));
        (await ReadInvoiceAsync(invoice.Id)).QbAttemptCount.Should().Be(0);
        await Worker.RunOnceAsync(default);
        (await ReadInvoiceAsync(invoice.Id)).QbInvoiceId.Should().Be("200");
    }

    [Fact]
    public async Task Send_ItemQuickBooksDoesNotHave_StopsBeforeCallingQuickBooks()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await InDbAsync(db => db.QuickBooksCatalogEntries.Where(e => e.Name == "Sales").ExecuteDeleteAsync());

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        var state = (await StatesAsync(invoice.Id)).Single();
        state.Status.Should().Be(QuickBooksExportStatus.NeedsAttention);
        state.LastError.Should().Contain("\"Sales\"");
        state.FixUrl.Should().Be("/settings/quickbooks");
        Api.Writes.Should().BeEmpty();
        Api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task Void_VoidsTheQuickBooksCopy_AndDeletesNothing()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await SendAsync(invoice.Id);

        await WithInvoiceServiceAsync(s => s.VoidAsync(invoice.Id, "Wrong customer"));
        (await ReadInvoiceAsync(invoice.Id)).QbExportStatus.Should().Be(QuickBooksExportStatus.Queued);
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbVoidedAt.Should().NotBeNull();
        saved.QbSyncToken.Should().Be("1");
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        var voidCall = Api.Writes.Last();
        voidCall.Parameters!["operation"].Should().Be("void");
        voidCall.Body.Should().Be("""{"Id":"200","SyncToken":"0"}""");
        _qb.Get("200")!["PrivateNote"]!.GetValue<string>().Should().Be("Voided");
        (await StatesAsync(invoice.Id)).Single().Label.Should().Be("Void in QuickBooks");
    }

    [Fact]
    public async Task Void_WithAPaymentInQuickBooks_IsRefused()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await SendAsync(invoice.Id);
        _qb.Get("200")!["Balance"] = 75m;

        await WithInvoiceServiceAsync(s => s.VoidAsync(invoice.Id, "Cancelled order"));
        await Worker.RunOnceAsync(default);

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.NeedsAttention);
        saved.QbLastErrorCode.Should().Be(QuickBooksErrorMessages.Paid);
        saved.QbLastError.Should().Contain("payment");
        saved.QbVoidedAt.Should().BeNull();
        Api.Writes.Should().BeEmpty("no void was sent");
    }

    [Fact]
    public async Task Void_OfAnInvoiceThatNeverReachedQuickBooks_QueuesNothing()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();

        await WithInvoiceServiceAsync(s => s.VoidAsync(invoice.Id, "Duplicate"));

        (await ReadInvoiceAsync(invoice.Id)).QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        (await Worker.RunOnceAsync(default)).Should().BeFalse();
    }

    [Fact]
    public async Task Worker_OnStart_PutsInterruptedSendsBackInLine()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await QueueAsync(invoice.Id);
        await InDbAsync(async db =>
        {
            (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).BeginQuickBooksAttempt(Now);
            await db.SaveChangesAsync();
        });

        (await Worker.ReleaseStuckAsync(default)).Should().Be(1);

        (await ReadInvoiceAsync(invoice.Id)).QbExportStatus.Should().Be(QuickBooksExportStatus.Queued);
    }

    [Fact]
    public async Task Worker_AppStoppedBetweenCreateAndRecord_NeverCreatesASecondCopy()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await QueueAsync(invoice.Id);
        using var stop = new CancellationTokenSource();
        _qb.AfterCreate = () =>
        {
            // QuickBooks made it, but the app is stopped before the answer is recorded.
            stop.Cancel();
            throw new OperationCanceledException(stop.Token);
        };

        var run = () => Worker.RunOnceAsync(stop.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        var cut = await ReadInvoiceAsync(invoice.Id);
        cut.QbExportStatus.Should().Be(QuickBooksExportStatus.Sending);
        cut.QbInvoiceId.Should().BeNull();
        _qb.AfterCreate = null;

        await using var restarted = BuildSendApp();
        var worker = restarted.GetRequiredService<QuickBooksSendWorker>();
        (await worker.ReleaseStuckAsync(default)).Should().Be(1);
        (await worker.RunOnceAsync(default)).Should().BeTrue();

        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbInvoiceId.Should().Be("200");
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        _qb.Count.Should().Be(1);
        Api.Writes.Count(w => w.Resource == "invoice").Should().Be(1);
    }

    [Fact]
    public async Task Worker_DoesNothingWhileQuickBooksIsNotConnected()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await QueueAsync(invoice.Id);
        await InDbAsync(db => db.QuickBooksConnections.ExecuteDeleteAsync());

        (await Worker.RunOnceAsync(default)).Should().BeFalse();

        (await ReadInvoiceAsync(invoice.Id)).QbExportStatus.Should().Be(QuickBooksExportStatus.Queued);
        Api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task SendNow_EmailsItOnce_ToTheOneOffAddress()
    {
        await ConnectAsync();
        await SaveSendModeAsync(QuickBooksSendMode.SendNow);
        var invoice = await SeedReadyInvoiceAsync();
        await WithSendServiceAsync(s => s.SetSendOptionsAsync(invoice.Id, false, "boss@example.com"));

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        Api.Writes.Should().HaveCount(2);
        JsonNode.Parse(Api.Writes[0].Body!)!["BillEmail"]!["Address"]!.GetValue<string>().Should().Be("boss@example.com");
        JsonNode.Parse(Api.Writes[0].Body!)!["EmailStatus"]!.GetValue<string>().Should().Be("NotSet");
        Api.Writes[1].Resource.Should().Be("invoice/200/send");
        Api.Writes[1].Body.Should().BeNull();
        Api.Writes[1].Parameters!["sendTo"].Should().Be("boss@example.com");
        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbSyncToken.Should().Be("1", "the send raised QuickBooks' version");
        saved.QbExportStatus.Should().Be(QuickBooksExportStatus.None);
    }

    [Fact]
    public async Task Held_IsCreatedUnsent()
    {
        await ConnectAsync();
        await SaveSendModeAsync(QuickBooksSendMode.SendNow);
        var invoice = await SeedReadyInvoiceAsync();
        await WithSendServiceAsync(s => s.SetSendOptionsAsync(invoice.Id, true, null));

        await QueueAsync(invoice.Id);
        await Worker.RunOnceAsync(default);

        Api.Writes.Should().ContainSingle().Which.Resource.Should().Be("invoice");
        JsonNode.Parse(Api.Writes[0].Body!)!["EmailStatus"]!.GetValue<string>().Should().Be("NotSet");
        (await ReadInvoiceAsync(invoice.Id)).QbInvoiceId.Should().Be("200");
    }

    [Fact]
    public async Task Queue_RefusesWhileNotConnected_AndNamesWhyAnInvoiceWaits()
    {
        var ready = await SeedReadyInvoiceAsync();
        var refused = await WithSendServiceAsync(s => s.QueueAsync([ready.Id]));
        refused.Single().Queued.Should().BeFalse();
        refused.Single().Reason.Should().Contain("isn't connected");

        await ConnectAsync();
        var waiting = await SeedReadyInvoiceAsync(billBeforeShip: false);
        var outcomes = await WithSendServiceAsync(s => s.QueueAsync([ready.Id, waiting.Id]));

        outcomes.Should().HaveCount(2);
        outcomes[0].Queued.Should().BeTrue();
        outcomes[1].Queued.Should().BeFalse();
        outcomes[1].InvoiceNumber.Should().Be(waiting.Number);
        outcomes[1].Reason.Should().StartWith("Not ready:");
    }

    [Fact]
    public async Task LinkExisting_FindsTheNumberOrSaysItIsNotThere()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();

        var missing = await WithSendServiceAsync(s => s.LinkExistingAsync(invoice.Id));
        missing.Linked.Should().BeFalse();
        (await ReadInvoiceAsync(invoice.Id)).QbExportedAt.Should().BeNull();

        _qb.Add("90", invoice.Number, invoice.QbCustomerId, 575m);
        var linked = await WithSendServiceAsync(s => s.LinkExistingAsync(invoice.Id));

        linked.Linked.Should().BeTrue();
        var saved = await ReadInvoiceAsync(invoice.Id);
        saved.QbInvoiceId.Should().Be("90");
        saved.QbExportedAt.Should().NotBeNull();
        (await ReadLogAsync()).Should().Contain(l => l.InvoiceId == invoice.Id && !l.Automatic);
    }

    [Fact]
    public async Task Cancel_TakesAQueuedInvoiceOutOfLine()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();
        await QueueAsync(invoice.Id);

        await WithSendServiceAsync(s => s.CancelAsync(invoice.Id));

        (await ReadInvoiceAsync(invoice.Id)).QbExportStatus.Should().Be(QuickBooksExportStatus.None);
        (await Worker.RunOnceAsync(default)).Should().BeFalse();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private sealed record SeededInvoice(Guid Id, string Number, string QbCustomerId);

    private ServiceProvider BuildSendApp()
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
        services.AddScoped<DocumentNumberService>();
        services.AddScoped<GeneralSettingsService>();
        services.AddSingleton(Options.Create(new InvoiceOptions()));
        services.AddSingleton<IEmailSender, NoopEmailSender>();
        services.AddScoped<DocumentEmailService>();
        services.AddScoped(_ => new TempPdfStorage(null!));
        services.AddScoped<InvoicePdfGenerator>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<QuickBooksMappingService>();
        services.AddScoped<QuickBooksInvoiceService>();
        services.AddScoped<QuickBooksCatalogService>();
        services.AddScoped<QuickBooksSendService>();
        services.AddScoped<QuickBooksInvoiceSender>();
        services.AddSingleton<QuickBooksSendWorker>();
        return services.BuildServiceProvider();
    }

    private async Task<T> InSendAppAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _app.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<T> WithSendServiceAsync<T>(Func<QuickBooksSendService, Task<T>> action) =>
        InSendAppAsync(sp => action(sp.GetRequiredService<QuickBooksSendService>()));

    private Task WithSendServiceAsync(Func<QuickBooksSendService, Task> action) =>
        InSendAppAsync(async sp =>
        {
            await action(sp.GetRequiredService<QuickBooksSendService>());
            return true;
        });

    private Task WithInvoiceServiceAsync(Func<InvoiceService, Task> action) =>
        InSendAppAsync(async sp =>
        {
            await action(sp.GetRequiredService<InvoiceService>());
            return true;
        });

    private async Task<QuickBooksQueueOutcome> QueueAsync(Guid invoiceId) =>
        (await WithSendServiceAsync(s => s.QueueAsync([invoiceId]))).Single();

    /// <summary>Queue and send one invoice, then forget the calls it made.</summary>
    private async Task SendAsync(Guid invoiceId)
    {
        (await QueueAsync(invoiceId)).Queued.Should().BeTrue();
        await Worker.RunOnceAsync(default);
        (await ReadInvoiceAsync(invoiceId)).QbInvoiceId.Should().NotBeNull();
        Api.Writes.Clear();
        Api.Queries.Clear();
        Api.Reads.Clear();
    }

    private Task<IReadOnlyList<QuickBooksSendState>> StatesAsync(Guid invoiceId) =>
        WithSendServiceAsync(s => s.GetStatesAsync([invoiceId]));

    private async Task<Invoice> ReadInvoiceAsync(Guid invoiceId)
    {
        using var scope = _app.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>()
            .Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
    }

    private Task SaveSendModeAsync(QuickBooksSendMode mode) =>
        InDbAsync(async db =>
        {
            var settings = QuickBooksSettings.CreateDefault(TestUserId, DateTime.UtcNow);
            settings.UpdatePush(
                new QuickBooksPushSettings(new Dictionary<PaymentTerms, string>(), null, null, mode),
                TestUserId,
                DateTime.UtcNow);
            db.QuickBooksSettings.Add(settings);
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Preview_OfAShipmentBornInvoice_ShowsTheLinesExactlyAsSent()
    {
        await ConnectAsync();
        var invoice = await SeedReadyInvoiceAsync();

        // An invoice generated from a shipment bills 600 of the 1,000 ordered.
        await InDbAsync(db => db.InvoiceLines
            .Where(l => l.InvoiceId == invoice.Id && l.SalesOrderLineId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Quantity, 600).SetProperty(l => l.LineTotal, 300m)));

        var preview = (await InSendAppAsync(sp => sp.GetRequiredService<QuickBooksInvoiceService>().GetPreviewAsync(invoice.Id)))!;
        var shown = preview.Lines.Single(l => l.IsProductLine);
        shown.Quantity.Should().Be(1000, "QuickBooks gets the ordered quantity");
        shown.Amount.Should().Be(300m, "the preview shows the billed amount, not 1,000 x 0.50");
        shown.SendsRate.Should().BeFalse();
        preview.Warnings.Should().Contain(w => w.Contains("ordered quantity (1,000)") && w.Contains("no rate"));

        (await QueueAsync(invoice.Id)).Queued.Should().BeTrue();
        (await Worker.RunOnceAsync(default)).Should().BeTrue();

        var sent = JsonNode.Parse(Api.Writes.Single(w => w.Resource == "invoice").Body!)!["Line"]!.AsArray();
        var sentProduct = sent.Single(l => l!["Description"]!.GetValue<string>() == shown.Description)!;
        sentProduct["Amount"]!.GetValue<decimal>().Should().Be(shown.Amount);
        sentProduct["SalesItemLineDetail"]!["Qty"]!.GetValue<decimal>().Should().Be(shown.Quantity);
        sentProduct["SalesItemLineDetail"]!.AsObject().ContainsKey("UnitPrice").Should().BeFalse();
        sent.Sum(l => l!["Amount"]!.GetValue<decimal>()).Should().Be(preview.Total, "the preview's total is what QuickBooks receives");
    }

    /// <summary>A customer linked to QuickBooks with a Billing contact, an order of 1,000 labels at
    /// 0.50 with $35 shipping quoted, and its invoice flagged bill-before-ship so it is Ready.</summary>
    private async Task<SeededInvoice> SeedReadyInvoiceAsync(bool billBeforeShip = true)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var qbCustomerId = $"QB-{suffix}";
        SalesOrder order = null!;

        await InDbAsync(async db =>
        {
            db.Suppliers.Add(Supplier.Create(supplierId, $"Send Sup {suffix}", $"SS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
            var customer = Customer.Create(customerId, $"Send Customer {suffix}", $"SC{suffix}"[..10],
                PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, TestUserId, now);
            customer.SetQuickBooksSetup(qbCustomerId, true, null, TestUserId, now);
            db.Customers.Add(customer);
            db.Contacts.Add(Contact.Create(Guid.NewGuid(), customerId, "Bea", "Biller", $"ap-{suffix}@example.com",
                null, "Billing", true, true, TestUserId, now));
            db.Stocks.Add(Stock.Create(stockId, $"ST{suffix}"[..10], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
                supplierId, null, 0.85m, 1000m, TestUserId, now));
            var product = Product.Create(
                Guid.NewGuid(), customerId, [customerId], $"SKU-{suffix}", null, "Send test labels", null,
                4, 3, 0.125m, stockId, InkSet.CMYK, "[]", null, null, null, TestUserId, now);
            db.Products.Add(product);
            foreach (var assignment in product.CustomerAssignments)
            {
                db.ProductCustomers.Add(assignment);
            }

            order = SalesOrder.CreateOpen(
                Guid.NewGuid(), $"SO-{suffix}", customerId, null, null, null, now, null, null, null,
                null, 35m, ShippingAddress.Empty, TestUserId, now);
            var line = SalesOrderLine.Create(
                Guid.NewGuid(), order.Id, 1, product.Id, "Send test labels", null, 1000, 0.50m, null, null, TestUserId, now);
            order.AddLine(line);
            db.SalesOrders.Add(order);
            db.SalesOrderLines.Add(line);
            await db.SaveChangesAsync();
        });

        var invoice = await InSendAppAsync(async sp =>
        {
            var invoices = sp.GetRequiredService<InvoiceService>();
            var created = await invoices.CreateFromSalesOrderAsync(order.Id);
            if (billBeforeShip)
            {
                await invoices.SetBillBeforeShipAsync(created.Id, true);
            }

            return created;
        });

        return new SeededInvoice(invoice.Id, invoice.InvoiceNumber, qbCustomerId);
    }

    private static QuickBooksApiException Fault(int status, string code, string message, string? detail = null) =>
        QuickBooksApiException.FromResponse(
            status,
            new JsonObject
            {
                ["Fault"] = new JsonObject
                {
                    ["Error"] = new JsonArray(new JsonObject { ["Message"] = message, ["Detail"] = detail ?? message, ["code"] = code }),
                    ["type"] = "ValidationFault"
                }
            }.ToJsonString(),
            $"tid-{code}");

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(
            string to,
            string subject,
            string body,
            IReadOnlyList<string>? attachmentPaths = null,
            string? cc = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>A small stand-in for a QuickBooks company's invoices: look-up by number, read,
    /// create (numbers unique, 8% tax on TAX lines, requestid replays the first answer), sparse
    /// update and void (both checking the sync token), and send.</summary>
    private sealed class FakeCompany
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, JsonObject> _invoices = [];
        private readonly Dictionary<string, string> _answersByRequestId = [];
        private int _nextId = 200;

        public Queue<QuickBooksApiException> CreateFailures { get; } = new();

        /// <summary>The next n look-ups find nothing (another writer is a step ahead).</summary>
        public int HideFromLookups { get; set; }

        /// <summary>The next n updates find the invoice changed in QuickBooks in the meantime.</summary>
        public int ConcurrentEditsBeforeUpdate { get; set; }

        /// <summary>Runs after QuickBooks has stored a new invoice, before it answers.</summary>
        public Action? AfterCreate { get; set; }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _invoices.Count;
                }
            }
        }

        public JsonObject? Get(string id)
        {
            lock (_lock)
            {
                return _invoices.GetValueOrDefault(id);
            }
        }

        public void Add(string id, string docNumber, string customerId, decimal total)
        {
            lock (_lock)
            {
                _invoices[id] = new JsonObject
                {
                    ["Id"] = id,
                    ["SyncToken"] = "0",
                    ["DocNumber"] = docNumber,
                    ["CustomerRef"] = new JsonObject { ["value"] = customerId, ["name"] = "Somebody" },
                    ["TotalAmt"] = total,
                    ["Balance"] = total,
                    ["TxnTaxDetail"] = new JsonObject { ["TotalTax"] = 0m }
                };
            }
        }

        public QuickBooksApiResponse? Query(string query, IReadOnlyDictionary<string, string>? parameters)
        {
            const string prefix = "select * from Invoice where DocNumber = '";
            if (!query.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            var number = query[prefix.Length..^1].Replace("\\'", "'", StringComparison.Ordinal);
            lock (_lock)
            {
                if (HideFromLookups > 0)
                {
                    HideFromLookups--;
                    return FakeApiClient.Ok("""{"QueryResponse":{}}""");
                }

                var found = _invoices.Values.FirstOrDefault(i => i["DocNumber"]!.GetValue<string>() == number);
                return FakeApiClient.Ok(found is null
                    ? """{"QueryResponse":{}}"""
                    : new JsonObject { ["QueryResponse"] = new JsonObject { ["Invoice"] = new JsonArray(found.DeepClone()) } }.ToJsonString());
            }
        }

        public QuickBooksApiResponse? Read(string resource, IReadOnlyDictionary<string, string>? parameters)
        {
            if (!resource.StartsWith("invoice/", StringComparison.Ordinal))
            {
                return null;
            }

            lock (_lock)
            {
                return Answer(_invoices[resource["invoice/".Length..]]);
            }
        }

        public QuickBooksApiResponse? Write(string resource, string? body, string? requestId, IReadOnlyDictionary<string, string>? parameters)
        {
            lock (_lock)
            {
                if (resource.StartsWith("invoice/", StringComparison.Ordinal) && resource.EndsWith("/send", StringComparison.Ordinal))
                {
                    var sent = _invoices[resource.Split('/')[1]];
                    sent["EmailStatus"] = "EmailSent";
                    Bump(sent);
                    return Answer(sent);
                }

                if (resource != "invoice")
                {
                    return null;
                }

                var request = JsonNode.Parse(body!)!.AsObject();
                if (parameters?.GetValueOrDefault("operation") == "void")
                {
                    var target = Current(request);
                    target["TotalAmt"] = 0m;
                    target["Balance"] = 0m;
                    target["PrivateNote"] = "Voided";
                    Bump(target);
                    return Answer(target);
                }

                if (request.ContainsKey("Id"))
                {
                    if (ConcurrentEditsBeforeUpdate > 0)
                    {
                        ConcurrentEditsBeforeUpdate--;
                        Bump(_invoices[request["Id"]!.GetValue<string>()]);
                    }

                    var target = Current(request);
                    target["Line"] = request["Line"]!.DeepClone();
                    Total(target);
                    Bump(target);
                    return Answer(target);
                }

                return Create(request, requestId);
            }
        }

        private QuickBooksApiResponse Create(JsonObject request, string? requestId)
        {
            if (requestId is not null && _answersByRequestId.TryGetValue(requestId, out var replay))
            {
                return FakeApiClient.Ok(replay);
            }

            if (CreateFailures.TryDequeue(out var failure))
            {
                throw failure;
            }

            var number = request["DocNumber"]!.GetValue<string>();
            if (_invoices.Values.FirstOrDefault(i => i["DocNumber"]!.GetValue<string>() == number) is { } taken)
            {
                throw Fault(400, "6140", "Duplicate Document Number Error",
                    $"Duplicate Document Number Error : You must specify a different number. This number has already been used. "
                    + $"DocNumber={number} is assigned to TxnType=Invoice with TxnId={taken["Id"]}");
            }

            var stored = request.DeepClone().AsObject();
            stored["Id"] = (_nextId++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            stored["SyncToken"] = "0";
            Total(stored);
            _invoices[stored["Id"]!.GetValue<string>()] = stored;
            AfterCreate?.Invoke();
            var answer = Answer(stored);
            if (requestId is not null)
            {
                _answersByRequestId[requestId] = answer.Body;
            }

            return answer;
        }

        private JsonObject Current(JsonObject request)
        {
            var target = _invoices[request["Id"]!.GetValue<string>()];
            if (target["SyncToken"]!.GetValue<string>() != request["SyncToken"]!.GetValue<string>())
            {
                throw Fault(400, "5010", "Stale Object Error",
                    "Stale Object Error : You and Craig Carlson were working on this at the same time.");
            }

            return target;
        }

        private static void Total(JsonObject invoice)
        {
            decimal net = 0m, taxable = 0m;
            foreach (var line in invoice["Line"]!.AsArray())
            {
                var amount = line!["Amount"]!.GetValue<decimal>();
                net += amount;
                if (line["SalesItemLineDetail"]?["TaxCodeRef"]?["value"]?.GetValue<string>() == "TAX")
                {
                    taxable += amount;
                }
            }

            var tax = Math.Round(taxable * 0.08m, 2, MidpointRounding.AwayFromZero);
            invoice["TxnTaxDetail"] = new JsonObject { ["TotalTax"] = tax };
            invoice["TotalAmt"] = net + tax;
            invoice["Balance"] = net + tax;
        }

        private static void Bump(JsonObject invoice) =>
            invoice["SyncToken"] = (int.Parse(invoice["SyncToken"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static QuickBooksApiResponse Answer(JsonObject invoice) =>
            FakeApiClient.Ok(new JsonObject { ["Invoice"] = invoice.DeepClone() }.ToJsonString());
    }
}
