using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabelsMis.Web.Tests.QuickBooks;

/// <summary>The test host plus the list cache and the settings row emptied before and after each
/// test, so other classes (the invoice preview tests) see the mapping defaults.</summary>
public abstract class QuickBooksListsTestHost : QuickBooksTestHost, IAsyncLifetime
{
    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await ClearListsAndSettingsAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await ClearListsAndSettingsAsync();
        await DisposeAsync();
    }

    protected Task ClearListsAndSettingsAsync() => InDbAsync(async db =>
    {
        await db.QuickBooksCatalogEntries.ExecuteDeleteAsync();
        await db.QuickBooksSettings.ExecuteDeleteAsync();
    });

    protected Task<T> WithCatalogAsync<T>(Func<QuickBooksCatalogService, Task<T>> action) =>
        InRequestAsync(services => action(new QuickBooksCatalogService(
            services.GetRequiredService<LabelsMisDbContext>(),
            services.GetRequiredService<QuickBooksGateway>(),
            services.GetRequiredService<ICurrentUserService>())));

    protected Task<T> WithMappingAsync<T>(Func<QuickBooksMappingService, Task<T>> action) =>
        InRequestAsync(services => action(new QuickBooksMappingService(
            services.GetRequiredService<LabelsMisDbContext>(),
            services.GetRequiredService<ICurrentUserService>())));

    protected async Task<List<QuickBooksCatalogEntry>> ReadCatalogAsync()
    {
        List<QuickBooksCatalogEntry> entries = [];
        await InDbAsync(async db => entries = await db.QuickBooksCatalogEntries.AsNoTracking()
            .OrderBy(e => e.Kind).ThenBy(e => e.QuickBooksId).ToListAsync());
        return entries;
    }

    /// <summary>Answers the list queries from <paramref name="pages"/> (kind → rows); a kind not
    /// given answers an empty list.</summary>
    protected void ScriptLists(IReadOnlyDictionary<string, string[]> pages) =>
        Api.OnQuery = (query, _) =>
        {
            var kind = query.Split(' ')[3];
            return pages.TryGetValue(kind, out var rows) ? FakeApiClient.Ok(Page(kind, rows)) : null;
        };

    protected static string Page(string kind, IReadOnlyCollection<string> rows) =>
        $$"""{"QueryResponse":{"{{kind}}":[{{string.Join(",", rows)}}],"startPosition":1,"maxResults":{{rows.Count}}},"time":"2026-11-13T08:00:00.000-08:00"}""";

    protected static string Item(string id, string name, bool taxable = false, string? fullName = null) =>
        $$"""{"Id":"{{id}}","Name":"{{name}}","FullyQualifiedName":"{{fullName ?? name}}","Active":true,"Type":"Service","Taxable":{{(taxable ? "true" : "false")}},"SyncToken":"0"}""";

    protected static string Class(string id, string name) =>
        $$"""{"Id":"{{id}}","Name":"{{name}}","SubClass":false,"FullyQualifiedName":"{{name}}","Active":true,"SyncToken":"0"}""";

    protected static string Term(string id, string name, int days) =>
        $$"""{"Id":"{{id}}","Name":"{{name}}","Active":true,"Type":"STANDARD","DueDays":{{days}},"SyncToken":"0"}""";
}

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksCatalogServiceTests : QuickBooksListsTestHost
{
    private const string ItemQuery = "select * from Item STARTPOSITION 1 MAXRESULTS 1000";
    private const string RecentInvoicesQuery = "select * from Invoice orderby MetaData.LastUpdatedTime desc maxresults 100";

    [Fact]
    public async Task RefreshAsync_ReadsItemsClassesAndTerms_AndCachesThem()
    {
        await ConnectAsync();
        ScriptLists(new Dictionary<string, string[]>
        {
            ["Item"] = [Item("1", "Sales", taxable: true), Item("2", "Shipping"), Item("11", "Pump", true, "Design:Fountains:Pump")],
            ["Class"] = [Class("756489", "Roll Label"), Class("756490", "Shrink Sleeves")],
            ["Term"] = [Term("3", "Net 30", 30)]
        });

        var result = await WithCatalogAsync(s => s.RefreshAsync());

        result.Should().Be(new QuickBooksCatalogRefreshResult(3, 2, 1, Added: 6, Removed: 0, result.RefreshedAt));
        Api.Queries.Select(q => q.Query).Should().Equal(
            ItemQuery,
            "select * from Class STARTPOSITION 1 MAXRESULTS 1000",
            "select * from Term STARTPOSITION 1 MAXRESULTS 1000");
        var lookup = await WithCatalogAsync(s => s.GetLookupAsync());
        lookup.Find(QuickBooksCatalogKind.Item, "sales").Should().Be(
            new QuickBooksListEntry(QuickBooksCatalogKind.Item, "1", "Sales", "Sales", true));
        lookup.Find(QuickBooksCatalogKind.Item, "Pump")!.FullName.Should().Be("Design:Fountains:Pump");
        lookup.Find(QuickBooksCatalogKind.Term, "Net 30")!.Id.Should().Be("3");
        (await WithCatalogAsync(s => s.GetCountsAsync())).Should().Be(new QuickBooksCatalogCounts(3, 2, 1));
        (await WithCatalogAsync(s => s.GetRefreshedAtAsync())).Should().BeCloseTo(result.RefreshedAt, TimeSpan.FromMilliseconds(1));
        (await ReadLogAsync()).Count(l => l.Operation == QuickBooksOperations.ListsRefresh).Should().Be(3);
    }

    [Fact]
    public async Task RefreshAsync_FullPage_AsksForTheNextPage()
    {
        await ConnectAsync();
        var firstPage = Enumerable.Range(1, 1000).Select(n => Item(n.ToString(), $"Item {n:0000}")).ToArray();
        Api.OnQuery = (query, _) => query switch
        {
            ItemQuery => FakeApiClient.Ok(Page("Item", firstPage)),
            "select * from Item STARTPOSITION 1001 MAXRESULTS 1000" => FakeApiClient.Ok(Page("Item", [Item("1001", "Last one"), Item("1002", "Really last")])),
            _ => null
        };

        var result = await WithCatalogAsync(s => s.RefreshAsync());

        result.Items.Should().Be(1002);
        Api.Queries.Select(q => q.Query).Should().HaveCount(4)
            .And.Contain("select * from Item STARTPOSITION 1001 MAXRESULTS 1000");
    }

    [Fact]
    public async Task RefreshAsync_Again_UpdatesInPlace_AddsNew_AndRemovesWhatQuickBooksNoLongerHas()
    {
        await ConnectAsync();
        ScriptLists(new Dictionary<string, string[]> { ["Item"] = [Item("1", "Sales"), Item("2", "Old item")] });
        await WithCatalogAsync(s => s.RefreshAsync());
        var firstRowId = (await ReadCatalogAsync()).Single(e => e.QuickBooksId == "1").Id;

        ScriptLists(new Dictionary<string, string[]> { ["Item"] = [Item("1", "Labels", true, "Sales:Labels"), Item("3", "New item")] });
        var result = await WithCatalogAsync(s => s.RefreshAsync());

        result.Should().Match<QuickBooksCatalogRefreshResult>(r => r.Items == 2 && r.Added == 1 && r.Removed == 1);
        var entries = await ReadCatalogAsync();
        entries.Select(e => (e.QuickBooksId, e.FullName, e.Taxable)).Should().Equal(
            ("1", "Sales:Labels", true), ("3", "New item", false));
        entries[0].Id.Should().Be(firstRowId, "a refresh updates the cached row in place");
    }

    [Fact]
    public async Task RefreshAsync_ReadFails_LeavesTheCacheAsItWas()
    {
        await ConnectAsync();
        ScriptLists(new Dictionary<string, string[]> { ["Item"] = [Item("1", "Sales")], ["Class"] = [Class("7", "Roll Label")] });
        await WithCatalogAsync(s => s.RefreshAsync());
        Api.OnQuery = (query, _) => query.Contains("from Class", StringComparison.Ordinal)
            ? throw QuickBooksApiException.FromResponse(
                503, """{"Fault":{"Error":[{"Message":"Service unavailable","code":"3001"}],"type":"SERVICE"}}""", "tid-503")
            : FakeApiClient.Ok(Page("Item", [Item("9", "Something else")]));

        var refresh = () => WithCatalogAsync(s => s.RefreshAsync());

        await refresh.Should().ThrowAsync<QuickBooksApiException>();
        (await ReadCatalogAsync()).Select(e => (e.Kind, e.QuickBooksId)).Should().Equal(
            (QuickBooksCatalogKind.Item, "1"), (QuickBooksCatalogKind.Class, "7"));
    }

    [Fact]
    public async Task RefreshAsync_NotConnected_ThrowsWithoutAskingQuickBooks()
    {
        var refresh = () => WithCatalogAsync(s => s.RefreshAsync());

        await refresh.Should().ThrowAsync<QuickBooksNotConnectedException>();
        Api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task FindCustomFieldsAsync_RecentInvoiceCarriesThePoField_ListsItFromTheInvoices()
    {
        await ConnectAsync();
        var readsBefore = Api.Reads.Count;
        Api.OnQuery = (query, _) => query == RecentInvoicesQuery
            ? FakeApiClient.Ok("""
                {"QueryResponse":{"Invoice":[
                  {"Id":"171","DocNumber":"LSQ8-pd76-PA","CustomField":[{"DefinitionId":"1000000001","Name":"P.O. Number","Type":"StringType","StringValue":"PO-E-pd76"}]},
                  {"Id":"170","DocNumber":"LSQ8-pd76-NO","CustomField":[]}],"startPosition":1,"maxResults":2,"totalCount":2}}
                """)
            : null;

        var found = await WithCatalogAsync(s => s.FindCustomFieldsAsync());

        found.Should().Be(new QuickBooksCustomFieldSearch(found.Fields, InvoicesRead: 2, FromPreferences: false));
        found.Fields.Should().Equal(new QuickBooksCustomFieldDefinition("1000000001", "P.O. Number", 1, Legacy: false));
        found.Suggested!.DefinitionId.Should().Be("1000000001");
        Api.Queries.Should().ContainSingle().Which.Parameters.Should()
            .Contain(new KeyValuePair<string, string>("include", "enhancedAllCustomFields"));
        Api.Reads.Should().HaveCount(readsBefore, "the company settings are only read when no invoice carries a field");
        (await ReadLogAsync()).Should().Contain(l => l.Operation == QuickBooksOperations.PoFieldFind);
    }

    [Fact]
    public async Task FindCustomFieldsAsync_NoInvoiceCarriesOne_FallsBackToTheLegacyFields()
    {
        await ConnectAsync();
        Api.OnQuery = (_, _) => FakeApiClient.Ok("""{"QueryResponse":{"Invoice":[{"Id":"1"},{"Id":"2"}]}}""");

        var found = await WithCatalogAsync(s => s.FindCustomFieldsAsync());

        found.FromPreferences.Should().BeTrue();
        found.InvoicesRead.Should().Be(2);
        found.Fields.Should().Equal(new QuickBooksCustomFieldDefinition("1", "P.O. Number", 0, Legacy: true));
        Api.Reads[^1].Resource.Should().Be("preferences");
    }

    [Fact]
    public async Task FindCustomFieldsAsync_NothingAnywhere_FindsNoField()
    {
        await ConnectAsync();
        Api.Preferences = """{"Preferences":{"SalesFormsPrefs":{"CustomTxnNumbers":true},"AccountingInfoPrefs":{"ClassTrackingPerTxnLine":true}}}""";

        var found = await WithCatalogAsync(s => s.FindCustomFieldsAsync());

        found.Fields.Should().BeEmpty();
        found.Suggested.Should().BeNull();
        found.InvoicesRead.Should().Be(0);
    }
}

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksMappingServiceTests : QuickBooksListsTestHost
{
    private Task SeedCatalogAsync(params (QuickBooksCatalogKind Kind, string Id, string Name, string FullName)[] entries) =>
        InDbAsync(async db =>
        {
            foreach (var (kind, id, name, fullName) in entries)
            {
                db.QuickBooksCatalogEntries.Add(QuickBooksCatalogEntry.Create(
                    Guid.NewGuid(), kind, id, name, fullName, kind == QuickBooksCatalogKind.Item ? false : null, TestUserId, Now));
            }

            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task GetPushSettingsAsync_NothingSaved_IsTheDefault()
    {
        var push = await WithMappingAsync(s => s.GetPushSettingsAsync());

        push.SendMode.Should().Be(QuickBooksSendMode.SendLater);
        push.Terms.Should().BeEmpty();
        push.PoFieldId.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePushAsync_SavesTermsPoFieldAndSendMode_AndSetPoFieldKeepsTheRest()
    {
        var terms = new Dictionary<PaymentTerms, string> { [PaymentTerms.Net30] = "Net 30", [PaymentTerms.DueOnReceipt] = "Due on receipt" };
        await WithMappingAsync(async s =>
        {
            await s.UpdatePushAsync(new QuickBooksPushSettings(terms, "1", "Custom 1", QuickBooksSendMode.SendNow));
            return true;
        });

        await WithMappingAsync(async s =>
        {
            await s.SetPoFieldAsync("1000000001", "P.O. Number");
            return true;
        });

        var push = await WithMappingAsync(s => s.GetPushSettingsAsync());
        push.Terms.Should().BeEquivalentTo(terms);
        push.SendMode.Should().Be(QuickBooksSendMode.SendNow);
        (push.PoFieldId, push.PoFieldName).Should().Be(("1000000001", "P.O. Number"));
        (await WithMappingAsync(s => s.GetMappingAsync())).Should().Be(QuickBooksMapping.Default, "the push settings leave the mapping alone");

        await WithMappingAsync(async s =>
        {
            await s.SaveSendingAsync(new Dictionary<PaymentTerms, string> { [PaymentTerms.Net15] = "Net 15" }, QuickBooksSendMode.Leave);
            return true;
        });

        var saved = await WithMappingAsync(s => s.GetPushSettingsAsync());
        saved.Terms.Should().BeEquivalentTo(new Dictionary<PaymentTerms, string> { [PaymentTerms.Net15] = "Net 15" });
        saved.SendMode.Should().Be(QuickBooksSendMode.Leave);
        saved.PoFieldId.Should().Be("1000000001", "saving terms and sending keeps the PO field");
    }

    [Fact]
    public async Task UpdateAsync_NameMatchingApartFromCaseOrParents_IsSavedAsQuickBooksSpellsIt()
    {
        await SeedCatalogAsync(
            (QuickBooksCatalogKind.Item, "1", "Sales", "Sales"),
            (QuickBooksCatalogKind.Item, "11", "Pump", "Design:Fountains:Pump"),
            (QuickBooksCatalogKind.Class, "7", "Roll Label", "Roll Label"));
        var typed = QuickBooksMapping.Default with { SalesItem = "sales", DesignItem = "pump", RollLabelClass = "ROLL LABEL" };

        await WithMappingAsync(async s =>
        {
            await s.UpdateAsync(typed);
            return true;
        });

        (await WithMappingAsync(s => s.GetMappingAsync())).Should().Be(typed with
        {
            SalesItem = "Sales",
            DesignItem = "Design:Fountains:Pump",
            RollLabelClass = "Roll Label"
        });
    }

    [Fact]
    public async Task GetLineOptionsAsync_ListsReadOnce_OffersQuickBooksNamesElseTheMappingNames()
    {
        var before = await WithMappingAsync(s => s.GetLineOptionsAsync(QuickBooksMapping.Default));
        before.Should().BeEquivalentTo(new QbLineOptions(QuickBooksMapping.Default.Items, QuickBooksMapping.Default.Classes));

        await SeedCatalogAsync(
            (QuickBooksCatalogKind.Item, "2", "Shipping", "Shipping"),
            (QuickBooksCatalogKind.Item, "11", "Pump", "Design:Fountains:Pump"),
            (QuickBooksCatalogKind.Term, "3", "Net 30", "Net 30"));

        var after = await WithMappingAsync(s => s.GetLineOptionsAsync(QuickBooksMapping.Default));

        after.Items.Should().Equal("Design:Fountains:Pump", "Shipping");
        after.Classes.Should().Equal(QuickBooksMapping.Default.Classes, "no class has been read from QuickBooks");
    }
}
