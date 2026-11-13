using System.Text.Json;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabelsMis.Web.Tests.QuickBooks;

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksCustomerServiceTests : QuickBooksTestHost
{
    private const string DuplicateNameFault = """
        {"Fault":{"Error":[{"Message":"Duplicate Name Exists Error","Detail":"The name supplied already exists. : Another customer, vendor or employee is already using this name. Please use a different name.","code":"6240","element":""}],"type":"ValidationFault"},"time":"2026-11-13T08:00:00.000-08:00"}
        """;

    // Unique per test so other test classes' customers (same database) never collide with these.
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly string _qbBase = Random.Shared.Next(100_000, 900_000).ToString();

    private string QbId(int n) => $"{_qbBase}{n}";

    private Task<T> WithServiceAsync<T>(Func<QuickBooksCustomerService, Task<T>> action) =>
        InRequestAsync(services => action(ActivatorUtilities.CreateInstance<QuickBooksCustomerService>(services)));

    private Task WithServiceAsync(Func<QuickBooksCustomerService, Task> action) =>
        WithServiceAsync(async s =>
        {
            await action(s);
            return true;
        });

    private async Task<Guid> SeedCustomerAsync(string name, string? quickBooksId = null, bool withDetails = false, bool active = true)
    {
        var id = Guid.NewGuid();
        await InDbAsync(async db =>
        {
            var customer = Customer.Create(id, name, $"QC{Guid.NewGuid():N}"[..10], PaymentTerms.Net30, false, 0.45m,
                CustomerStatus.Active, null, null, TestUserId, Now);
            if (withDetails)
            {
                customer.AddContact(Contact.Create(Guid.NewGuid(), id, "Bea", "Payable", "ap@example.com", "602-555-0199",
                    null, isPrimary: true, isBillingContact: true, TestUserId, Now));
                customer.AddAddress(Address.Create(Guid.NewGuid(), id, AddressType.Billing, "1 Bill St", null, "Phoenix", "AZ",
                    "85001", "US", true, TestUserId, Now));
            }

            if (quickBooksId is not null)
            {
                customer.LinkQuickBooksCustomer(quickBooksId, $"QB {quickBooksId}", TestUserId, Now);
            }

            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            if (!active)
            {
                await db.Customers.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
            }
        });
        return id;
    }

    private async Task<Customer> ReadCustomerAsync(Guid id)
    {
        Customer? customer = null;
        await InDbAsync(async db => customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == id));
        return customer!;
    }

    private static string Row(string id, string displayName, bool active = true, string? company = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["DisplayName"] = displayName,
            ["CompanyName"] = company,
            ["Active"] = active
        });

    private static QuickBooksApiResponse Page(params string[] rows) =>
        FakeApiClient.Ok("{\"QueryResponse\":{\"Customer\":[" + string.Join(",", rows) + "],\"startPosition\":1,\"maxResults\":"
                         + rows.Length + "}}");

    private static QuickBooksApiResponse One(string row) => FakeApiClient.Ok($$"""{"Customer":{{row}},"time":"x"}""");

    [Fact]
    public async Task SearchAsync_AsksForActiveCustomersWhoseNameContainsTheText()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"Search {_suffix}");
        Api.OnQuery = (_, _) => Page(Row(QbId(2), "Zeta Labels"), Row(QbId(1), "Acme Labels"));

        var found = await WithServiceAsync(s => s.SearchAsync(" labels ", customerId));

        Api.Queries.Should().ContainSingle().Which.Query.Should().Be(
            "select * from Customer where Active = true and DisplayName like '%labels%' startposition 1 maxresults 20");
        found.Select(c => c.DisplayName).Should().Equal("Acme Labels", "Zeta Labels");
        (await ReadLogAsync()).Should().Contain(l =>
            l.Operation == QuickBooksCustomerOperations.Search && l.Succeeded && l.CustomerId == customerId);
    }

    [Fact]
    public async Task SearchAsync_WithNothingTyped_AsksQuickBooksNothing()
    {
        await ConnectAsync();

        var found = await WithServiceAsync(s => s.SearchAsync("  "));

        found.Should().BeEmpty();
        Api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhenQuickBooksIsNotConnected_SaysSo()
    {
        await SaveKeysAsync();

        var act = () => WithServiceAsync(s => s.SearchAsync("acme"));

        await act.Should().ThrowAsync<QuickBooksNotConnectedException>();
        Api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailabilityAsync_FollowsTheConnection()
    {
        await SaveKeysAsync();
        var before = await WithServiceAsync(s => s.GetAvailabilityAsync());
        await WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-1", RealmId, RedirectUri));
        var after = await WithServiceAsync(s => s.GetAvailabilityAsync());

        before.Connected.Should().BeFalse();
        before.Problem.Should().Be(QuickBooksTokenService.NotConnectedMessage);
        after.Should().Be(new QuickBooksCustomerAvailability(true, QuickBooksEnvironment.Sandbox, null));
        after.OpenUrl("58").Should().Be("https://app.sandbox.qbo.intuit.com/app/customerdetail?nameId=58");
    }

    [Fact]
    public async Task LinkAsync_ReadsTheQuickBooksCustomerAndKeepsItsName()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"Link {_suffix}");
        Api.OnRead = (resource, _) => resource == $"customer/{QbId(1)}" ? One(Row(QbId(1), "Acme Labels")) : null;

        var result = await WithServiceAsync(s => s.LinkAsync(customerId, $" {QbId(1)} "));

        result.Customer.DisplayName.Should().Be("Acme Labels");
        result.AlsoLinkedTo.Should().BeEmpty();
        var customer = await ReadCustomerAsync(customerId);
        customer.QuickBooksCustomerId.Should().Be(QbId(1));
        customer.QuickBooksCustomerName.Should().Be("Acme Labels");
        customer.ModifiedById.Should().Be(TestUserId);
        (await ReadLogAsync()).Should().Contain(l =>
            l.Operation == QuickBooksCustomerOperations.Read && l.CustomerId == customerId);
    }

    [Fact]
    public async Task LinkAsync_ToAnInactiveQuickBooksCustomer_Refuses()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"Inactive {_suffix}");
        Api.OnRead = (resource, _) => resource.StartsWith("customer/", StringComparison.Ordinal)
            ? One(Row(QbId(1), "Old Labels", active: false))
            : null;

        var act = () => WithServiceAsync(s => s.LinkAsync(customerId, QbId(1)));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inactive in QuickBooks*");
        (await ReadCustomerAsync(customerId)).QuickBooksCustomerId.Should().BeNull();
    }

    [Fact]
    public async Task LinkAsync_ToAnIdQuickBooksDoesNotHave_SaysSo()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"Missing {_suffix}");
        Api.OnRead = (resource, _) =>
        {
            if (resource.StartsWith("customer/", StringComparison.Ordinal))
            {
                throw QuickBooksApiException.FromResponse(400,
                    """{"Fault":{"Error":[{"Message":"Object Not Found","Detail":"Object Not Found : Something you're trying to use has been made inactive.","code":"610"}]}}""",
                    "tid-610");
            }

            return null;
        };

        var act = () => WithServiceAsync(s => s.LinkAsync(customerId, "999999"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("QuickBooks has no customer with id 999999.");
    }

    [Fact]
    public async Task LinkAsync_ToACustomerAnotherLabelShopCustomerUses_LinksAndNamesTheOther()
    {
        await ConnectAsync();
        await SeedCustomerAsync($"First {_suffix}", QbId(1));
        var customerId = await SeedCustomerAsync($"Second {_suffix}");
        Api.OnRead = (resource, _) => resource == $"customer/{QbId(1)}" ? One(Row(QbId(1), "Acme Labels")) : null;

        var result = await WithServiceAsync(s => s.LinkAsync(customerId, QbId(1)));

        result.AlsoLinkedTo.Should().Equal($"First {_suffix}");
        (await ReadCustomerAsync(customerId)).QuickBooksCustomerId.Should().Be(QbId(1));
    }

    [Fact]
    public async Task LinkByNameAsync_LinksTheActiveCustomerWithThatExactName()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"By name {_suffix}");
        Api.OnQuery = (query, _) => query.Contains("DisplayName = 'Amy\\'s Birds'", StringComparison.Ordinal)
            ? Page(Row(QbId(3), "Amy's Birds"))
            : null;

        await WithServiceAsync(s => s.LinkByNameAsync(customerId, "Amy's Birds"));

        (await ReadCustomerAsync(customerId)).QuickBooksCustomerId.Should().Be(QbId(3));
    }

    [Fact]
    public async Task UnlinkAsync_ClearsTheIdAndTheName()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"Unlink {_suffix}", QbId(1));

        await WithServiceAsync(s => s.UnlinkAsync(customerId));

        var customer = await ReadCustomerAsync(customerId);
        customer.QuickBooksCustomerId.Should().BeNull();
        customer.QuickBooksCustomerName.Should().BeNull();
        Api.Reads.Should().NotContain(r => r.Resource.StartsWith("customer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_SendsTheCustomerWithARequestIdAndLinksWhatQuickBooksMade()
    {
        await ConnectAsync();
        var name = $"Create {_suffix}";
        var customerId = await SeedCustomerAsync(name, withDetails: true);
        Api.OnWrite = (resource, _, _, _) => resource == "customer" ? One(Row(QbId(7), name)) : null;

        var result = await WithServiceAsync(s => s.CreateAsync(customerId));

        result.Outcome.Should().Be(QuickBooksCustomerCreateOutcome.Created);
        var write = Api.Writes.Should().ContainSingle().Subject;
        write.Resource.Should().Be("customer");
        write.RequestId.Should().HaveLength(QuickBooksCustomerPayload.RequestIdLength);
        using var body = JsonDocument.Parse(write.Body!);
        body.RootElement.GetProperty("DisplayName").GetString().Should().Be(name);
        body.RootElement.GetProperty("PrimaryEmailAddr").GetProperty("Address").GetString().Should().Be("ap@example.com");
        body.RootElement.GetProperty("BillAddr").GetProperty("City").GetString().Should().Be("Phoenix");
        var customer = await ReadCustomerAsync(customerId);
        customer.QuickBooksCustomerId.Should().Be(QbId(7));
        customer.QuickBooksCustomerName.Should().Be(name);
        (await ReadLogAsync()).Should().Contain(l =>
            l.Operation == QuickBooksCustomerOperations.Create && l.Succeeded && l.CustomerId == customerId);
    }

    [Fact]
    public async Task CreateAsync_DoubleClicked_MakesOneQuickBooksCustomer()
    {
        await ConnectAsync();
        var name = $"Double {_suffix}";
        var customerId = await SeedCustomerAsync(name, withDetails: true);
        // Stands in for Intuit: a repeated requestid gets the first answer back.
        var made = new Dictionary<string, QuickBooksApiResponse>();
        var next = 0;
        Api.OnWrite = (resource, _, requestId, _) =>
        {
            lock (made)
            {
                if (!made.TryGetValue(requestId!, out var answer))
                {
                    answer = One(Row(QbId(10 + Interlocked.Increment(ref next)), name));
                    made[requestId!] = answer;
                }

                return answer;
            }
        };

        var results = await Task.WhenAll(
            WithServiceAsync(s => s.CreateAsync(customerId)),
            WithServiceAsync(s => s.CreateAsync(customerId)));

        Api.Writes.Should().HaveCount(2);
        Api.Writes.Select(w => w.RequestId).Distinct().Should().ContainSingle();
        made.Should().ContainSingle();
        results.Select(r => r.Customer!.Id).Should().OnlyContain(id => id == QbId(11));
        (await ReadCustomerAsync(customerId)).QuickBooksCustomerId.Should().Be(QbId(11));
    }

    [Fact]
    public async Task CreateAsync_ForALinkedCustomer_RefusesWithoutCallingQuickBooks()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"Linked {_suffix}", QbId(1));

        var act = () => WithServiceAsync(s => s.CreateAsync(customerId));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already linked*Unlink it first*");
        Api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenTheNameIsTaken_OffersTheQuickBooksCustomerThatHasIt()
    {
        await ConnectAsync();
        var name = $"Taken {_suffix}";
        var customerId = await SeedCustomerAsync(name);
        Api.OnWrite = (_, _, _, _) => throw QuickBooksApiException.FromResponse(400, DuplicateNameFault, "tid-6240");
        Api.OnQuery = (query, _) => query == QuickBooksCustomerQueries.ByDisplayName(name) ? Page(Row(QbId(4), name)) : null;

        var result = await WithServiceAsync(s => s.CreateAsync(customerId));

        result.Outcome.Should().Be(QuickBooksCustomerCreateOutcome.DuplicateName);
        result.Customer!.Id.Should().Be(QbId(4));
        result.Message.Should().Contain("already has a customer named");
        (await ReadCustomerAsync(customerId)).QuickBooksCustomerId.Should().BeNull();
        (await ReadLogAsync()).Should().Contain(l =>
            l.Operation == QuickBooksCustomerOperations.Create && !l.Succeeded && l.ErrorCode == "6240");
    }

    [Fact]
    public async Task CreateAsync_WhenTheNameBelongsToSomethingElse_SaysWhereToLook()
    {
        await ConnectAsync();
        var customerId = await SeedCustomerAsync($"Vendor {_suffix}");
        Api.OnWrite = (_, _, _, _) => throw QuickBooksApiException.FromResponse(400, DuplicateNameFault, "tid-6240");

        var result = await WithServiceAsync(s => s.CreateAsync(customerId));

        result.Outcome.Should().Be(QuickBooksCustomerCreateOutcome.DuplicateName);
        result.Customer.Should().BeNull();
        result.Message.Should().Contain("an inactive customer, a vendor or an employee");
    }

    [Fact]
    public async Task ListActiveAsync_ReadsEveryPage()
    {
        await ConnectAsync();
        var firstPage = Enumerable.Range(1, 1000).Select(i => Row($"p{i}", $"Customer {i:0000}")).ToArray();
        Api.OnQuery = (query, _) => query.Contains("startposition 1 ", StringComparison.Ordinal)
            ? Page(firstPage)
            : query.Contains("startposition 1001 ", StringComparison.Ordinal)
                ? Page(Row("q1", "Last One"), Row("q2", "Gone", active: false))
                : null;

        var all = await WithServiceAsync(s => s.ListActiveAsync());

        all.Should().HaveCount(1001);
        Api.Queries.Select(q => q.Query).Should().Equal(
            QuickBooksCustomerQueries.ActivePage(1), QuickBooksCustomerQueries.ActivePage(1001));
    }

    [Fact]
    public async Task GetMatchViewAsync_ProposesExactMatchesAndSkipsCustomersLinkedElsewhere()
    {
        await ConnectAsync();
        var amy = await SeedCustomerAsync($"Amy's Birds {_suffix}");
        var other = await SeedCustomerAsync($"Other Co {_suffix}");
        await SeedCustomerAsync($"Already {_suffix}", QbId(2));
        var gone = await SeedCustomerAsync($"Gone Co {_suffix}", active: false);
        Api.OnQuery = (_, _) => Page(
            Row(QbId(1), $"AMYS BIRDS {_suffix}"),
            Row(QbId(2), $"Other Co {_suffix}"),
            Row(QbId(3), $"Gone Co {_suffix}"));

        var view = await WithServiceAsync(s => s.GetMatchViewAsync());

        view.Matches.Single(m => m.CustomerId == amy).Proposed!.Id.Should().Be(QbId(1));
        view.Matches.Single(m => m.CustomerId == other).Kind.Should().Be(QuickBooksMatchKind.None);
        view.Matches.Should().NotContain(m => m.CustomerId == gone);
        view.Unclaimed.Select(c => c.Id).Should().Contain(QbId(1)).And.NotContain(QbId(2));
        view.QuickBooksCount.Should().Be(3);
        Api.Queries.Should().ContainSingle();
    }

    [Fact]
    public async Task LinkAllAsync_LinksTheConfirmedPairsAndNamesTheOnesItSkipped()
    {
        await ConnectAsync();
        var amy = await SeedCustomerAsync($"Amy {_suffix}");
        var bob = await SeedCustomerAsync($"Bob {_suffix}");
        var cal = await SeedCustomerAsync($"Cal {_suffix}");
        await SeedCustomerAsync($"Elsewhere {_suffix}", QbId(2));
        Api.OnQuery = (_, _) => Page(Row(QbId(1), "Amy QB"), Row(QbId(2), "Bob QB"));

        var result = await WithServiceAsync(s => s.LinkAllAsync(
        [
            new QuickBooksCustomerPair(amy, QbId(1)),
            new QuickBooksCustomerPair(bob, QbId(2)),
            new QuickBooksCustomerPair(cal, QbId(9))
        ]));

        result.Linked.Should().Be(1);
        result.Skipped.Should().BeEquivalentTo(
            $"Bob {_suffix}: \u201cBob QB\u201d is already linked to another customer.",
            $"Cal {_suffix}: the QuickBooks customer is no longer active.");
        var linked = await ReadCustomerAsync(amy);
        linked.QuickBooksCustomerId.Should().Be(QbId(1));
        linked.QuickBooksCustomerName.Should().Be("Amy QB");
        (await ReadCustomerAsync(bob)).QuickBooksCustomerId.Should().BeNull();
    }
}
