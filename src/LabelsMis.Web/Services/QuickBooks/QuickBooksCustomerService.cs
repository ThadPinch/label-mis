using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>Whether the customer pages can ask QuickBooks anything right now, and which
/// QuickBooks (sandbox or production) "Open in QuickBooks" points at.</summary>
public sealed record QuickBooksCustomerAvailability(bool Connected, QuickBooksEnvironment Environment, string? Problem)
{
    public string? OpenUrl(string? quickBooksCustomerId) => QuickBooksLinks.Customer(Environment, quickBooksCustomerId);
}

/// <summary>A link made. <see cref="AlsoLinkedTo"/> names other LabelShop customers already
/// linked to the same QuickBooks customer (allowed, but worth saying).</summary>
public sealed record QuickBooksCustomerLinkResult(QuickBooksCustomerSummary Customer, IReadOnlyList<string> AlsoLinkedTo);

public enum QuickBooksCustomerCreateOutcome
{
    Created,

    /// <summary>QuickBooks refused the name (6240): something there already uses it.</summary>
    DuplicateName
}

/// <summary>What Create in QuickBooks did. For <see cref="QuickBooksCustomerCreateOutcome.Created"/>
/// <see cref="Customer"/> is the new, now linked, customer. For a duplicate name it is the active
/// QuickBooks customer holding the name (offer to link it), or null when the name belongs to an
/// inactive customer, a vendor or an employee; <see cref="Message"/> says which.</summary>
public sealed record QuickBooksCustomerCreateResult(
    QuickBooksCustomerCreateOutcome Outcome,
    QuickBooksCustomerSummary? Customer,
    string? Message);

/// <summary>The Match customers page: the active LabelShop customers with no link and what each
/// could be linked to, plus the QuickBooks customers nobody is linked to yet (for the search).</summary>
public sealed record QuickBooksCustomerMatchView(
    IReadOnlyList<QuickBooksCustomerMatch> Matches,
    IReadOnlyList<QuickBooksCustomerSummary> Unclaimed,
    int QuickBooksCount,
    int LinkedCount)
{
    public int UnlinkedCount => Matches.Count;
    public int ExactCount => Matches.Count(m => m.Kind == QuickBooksMatchKind.Exact);
}

/// <summary>A link the Match page asks for: LabelShop customer → QuickBooks customer id.</summary>
public sealed record QuickBooksCustomerPair(Guid CustomerId, string QuickBooksCustomerId);

public sealed record QuickBooksLinkAllResult(int Linked, IReadOnlyList<string> Skipped);

/// <summary>Links LabelShop customers to QuickBooks customers: search QuickBooks' list, link one,
/// or create the customer in QuickBooks.</summary>
public class QuickBooksCustomerService(
    LabelsMisDbContext db,
    QuickBooksGateway gateway,
    ICurrentUserService currentUser,
    TimeProvider time)
{
    private const int MaxPages = 100;

    private readonly LabelsMisDbContext _db = db;
    private readonly QuickBooksGateway _gateway = gateway;
    private readonly ICurrentUserService _currentUser = currentUser;

    public async Task<QuickBooksCustomerAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _db.QuickBooksConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return new QuickBooksCustomerAvailability(false, QuickBooksEnvironment.Sandbox, QuickBooksTokenService.NotConnectedMessage);
        }

        var problem = connection.State switch
        {
            QuickBooksConnectionState.Connected => null,
            QuickBooksConnectionState.NeedsReconnect => QuickBooksTokenService.NeedsReconnectMessage,
            _ => QuickBooksTokenService.NotConnectedMessage
        };
        return new QuickBooksCustomerAvailability(problem is null, connection.Environment, problem);
    }

    /// <summary>Active QuickBooks customers whose display name contains <paramref name="text"/>,
    /// at most <see cref="QuickBooksCustomerQueries.SearchLimit"/>, sorted by name.</summary>
    public async Task<IReadOnlyList<QuickBooksCustomerSummary>> SearchAsync(
        string? text, Guid? customerId = null, CancellationToken cancellationToken = default)
    {
        var query = QuickBooksCustomerQueries.Search(text);
        if (query is null)
        {
            return [];
        }

        var response = await _gateway.QueryAsync(
            query, QuickBooksCustomerOperations.Search, _gateway.CallerFor(customerId: customerId), cancellationToken: cancellationToken);
        return QuickBooksCustomerParser.ParsePage(response.Body).Customers
            .Where(c => c.Active)
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every active QuickBooks customer, read a thousand at a time.</summary>
    public async Task<IReadOnlyList<QuickBooksCustomerSummary>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<QuickBooksCustomerSummary>();
        var caller = _gateway.CallerFor();
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _gateway.QueryAsync(
                QuickBooksCustomerQueries.ActivePage(page * QuickBooksCustomerQueries.PageSize + 1),
                QuickBooksCustomerOperations.List,
                caller,
                cancellationToken: cancellationToken);
            var (customers, rows) = QuickBooksCustomerParser.ParsePage(response.Body);
            all.AddRange(customers.Where(c => c.Active));
            if (rows < QuickBooksCustomerQueries.PageSize)
            {
                break;
            }
        }

        return all.DistinctBy(c => c.Id).OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Links the customer to the QuickBooks customer with this id, read from QuickBooks
    /// first so the id is known to exist and its current name is kept.</summary>
    public async Task<QuickBooksCustomerLinkResult> LinkAsync(
        Guid customerId, string quickBooksCustomerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(quickBooksCustomerId))
        {
            throw new ArgumentException("Pick the QuickBooks customer to link.", nameof(quickBooksCustomerId));
        }

        var customer = await LoadAsync(customerId, cancellationToken);
        var id = quickBooksCustomerId.Trim();
        QuickBooksApiResponse response;
        try
        {
            response = await _gateway.ReadAsync(
                $"customer/{Uri.EscapeDataString(id)}",
                QuickBooksCustomerOperations.Read,
                _gateway.CallerFor(customerId: customerId),
                cancellationToken: cancellationToken);
        }
        catch (QuickBooksApiException ex) when (ex.Code == "610")
        {
            throw new InvalidOperationException($"QuickBooks has no customer with id {id}.", ex);
        }

        var found = QuickBooksCustomerParser.ParseOne(response.Body)
                    ?? throw new InvalidOperationException($"QuickBooks has no customer with id {id}.");
        if (!found.Active)
        {
            throw new InvalidOperationException(
                $"\u201c{found.DisplayName}\u201d is inactive in QuickBooks. Make it active there first, then link it.");
        }

        return await LinkFoundAsync(customer, found, cancellationToken);
    }

    /// <summary>Links the customer to the active QuickBooks customer with exactly this display name
    /// (the Match page's search box hands over a name).</summary>
    public async Task<QuickBooksCustomerLinkResult> LinkByNameAsync(
        Guid customerId, string displayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Type or pick the QuickBooks customer's name.", nameof(displayName));
        }

        var customer = await LoadAsync(customerId, cancellationToken);
        var found = await FindByDisplayNameAsync(displayName, customerId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"QuickBooks has no active customer named \u201c{displayName.Trim()}\u201d. Pick a name from the list.");
        return await LinkFoundAsync(customer, found, cancellationToken);
    }

    /// <summary>Removes the link. Nothing changes in QuickBooks.</summary>
    public async Task UnlinkAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await LoadAsync(customerId, cancellationToken);
        customer.UnlinkQuickBooksCustomer(UserId, Now);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Creates the customer in QuickBooks from LabelShop's record and links it. The
    /// <c>requestid</c> comes from the customer and the body, so a double click (or a retry after
    /// the answer was lost) returns the customer the first call made instead of a second one.</summary>
    public async Task<QuickBooksCustomerCreateResult> CreateAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Contacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer not found.");
        if (customer.QuickBooksCustomerId is not null)
        {
            throw new InvalidOperationException(
                $"This customer is already linked to QuickBooks customer {Describe(customer.QuickBooksCustomerName, customer.QuickBooksCustomerId)}. Unlink it first to create a new one.");
        }

        var create = QuickBooksCustomerPayload.Build(customer);
        QuickBooksApiResponse response;
        try
        {
            response = await _gateway.WriteAsync(
                "customer",
                create.Json,
                QuickBooksCustomerOperations.Create,
                requestId: create.RequestId,
                caller: _gateway.CallerFor(customerId: customerId),
                cancellationToken: cancellationToken);
        }
        catch (QuickBooksApiException ex) when (ex.Code == "6240")
        {
            var existing = await FindByDisplayNameAsync(create.DisplayName, customerId, cancellationToken);
            var message = existing is not null
                ? $"QuickBooks already has a customer named \u201c{existing.DisplayName}\u201d. Link to it, or change the name here and create again."
                : $"QuickBooks already uses the name \u201c{create.DisplayName}\u201d for an inactive customer, a vendor or an employee. "
                  + "Make the customer active in QuickBooks and link it, or change the name here and create again.";
            return new QuickBooksCustomerCreateResult(QuickBooksCustomerCreateOutcome.DuplicateName, existing, message);
        }

        var created = QuickBooksCustomerParser.ParseOne(response.Body)
                      ?? throw new InvalidOperationException(
                          "QuickBooks did not send the new customer back. Search QuickBooks for it before trying again.");
        customer.LinkQuickBooksCustomer(created.Id, created.DisplayName, UserId, Now);
        await _db.SaveChangesAsync(cancellationToken);
        return new QuickBooksCustomerCreateResult(QuickBooksCustomerCreateOutcome.Created, created, null);
    }

    /// <summary>Reads QuickBooks' active customers once and proposes a match for every active
    /// LabelShop customer that has no link.</summary>
    public async Task<QuickBooksCustomerMatchView> GetMatchViewAsync(CancellationToken cancellationToken = default)
    {
        var quickBooks = await ListActiveAsync(cancellationToken);
        var linkedIds = await LinkedIdsAsync(cancellationToken);
        var unlinked = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive && c.QuickBooksCustomerId == null)
            .OrderBy(c => c.Name)
            .Select(c => new UnlinkedCustomer(c.Id, c.Name))
            .ToListAsync(cancellationToken);
        var linkedCount = await _db.Customers.AsNoTracking()
            .CountAsync(c => c.IsActive && c.QuickBooksCustomerId != null, cancellationToken);

        var matches = QuickBooksCustomerMatcher.Match(unlinked, quickBooks, linkedIds);
        var unclaimed = quickBooks.Where(q => !linkedIds.Contains(q.Id)).ToList();
        return new QuickBooksCustomerMatchView(matches, unclaimed, quickBooks.Count, linkedCount);
    }

    /// <summary>Makes the links the person confirmed on the Match page. QuickBooks' list is read
    /// once more, so a customer made inactive there, or linked by someone else in the meantime, is
    /// skipped and named instead of linked.</summary>
    public async Task<QuickBooksLinkAllResult> LinkAllAsync(
        IReadOnlyList<QuickBooksCustomerPair> pairs, CancellationToken cancellationToken = default)
    {
        var wanted = pairs.Where(p => !string.IsNullOrWhiteSpace(p.QuickBooksCustomerId)).DistinctBy(p => p.CustomerId).ToList();
        if (wanted.Count == 0)
        {
            return new QuickBooksLinkAllResult(0, []);
        }

        var quickBooks = (await ListActiveAsync(cancellationToken)).ToDictionary(q => q.Id, StringComparer.Ordinal);
        var linkedIds = await LinkedIdsAsync(cancellationToken);
        var ids = wanted.Select(p => p.CustomerId).ToList();
        var customers = await _db.Customers.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);

        var skipped = new List<string>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var linked = 0;
        foreach (var pair in wanted)
        {
            var qbId = pair.QuickBooksCustomerId.Trim();
            if (!customers.TryGetValue(pair.CustomerId, out var customer))
            {
                continue;
            }

            if (customer.QuickBooksCustomerId is not null)
            {
                skipped.Add($"{customer.Name}: already linked.");
            }
            else if (!quickBooks.TryGetValue(qbId, out var found))
            {
                skipped.Add($"{customer.Name}: the QuickBooks customer is no longer active.");
            }
            else if (linkedIds.Contains(qbId) || !claimed.Add(qbId))
            {
                skipped.Add($"{customer.Name}: \u201c{found.DisplayName}\u201d is already linked to another customer.");
            }
            else
            {
                customer.LinkQuickBooksCustomer(found.Id, found.DisplayName, UserId, Now);
                linked++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new QuickBooksLinkAllResult(linked, skipped);
    }

    private async Task<QuickBooksCustomerLinkResult> LinkFoundAsync(
        Customer customer, QuickBooksCustomerSummary found, CancellationToken cancellationToken)
    {
        var others = await _db.Customers.AsNoTracking()
            .Where(c => c.Id != customer.Id && c.QuickBooksCustomerId == found.Id)
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);
        customer.LinkQuickBooksCustomer(found.Id, found.DisplayName, UserId, Now);
        await _db.SaveChangesAsync(cancellationToken);
        return new QuickBooksCustomerLinkResult(found, others);
    }

    private async Task<QuickBooksCustomerSummary?> FindByDisplayNameAsync(
        string displayName, Guid customerId, CancellationToken cancellationToken)
    {
        var response = await _gateway.QueryAsync(
            QuickBooksCustomerQueries.ByDisplayName(displayName),
            QuickBooksCustomerOperations.Search,
            _gateway.CallerFor(customerId: customerId),
            cancellationToken: cancellationToken);
        return QuickBooksCustomerParser.ParsePage(response.Body).Customers
            .FirstOrDefault(c => c.Active && string.Equals(c.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HashSet<string>> LinkedIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await _db.Customers.AsNoTracking()
            .Where(c => c.QuickBooksCustomerId != null)
            .Select(c => c.QuickBooksCustomerId!)
            .Distinct()
            .ToListAsync(cancellationToken);
        return new HashSet<string>(ids.Select(i => i.Trim()), StringComparer.Ordinal);
    }

    private async Task<Customer> LoadAsync(Guid customerId, CancellationToken cancellationToken) =>
        await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
        ?? throw new KeyNotFoundException("Customer not found.");

    private static string Describe(string? name, string id) =>
        string.IsNullOrWhiteSpace(name) ? id : $"\u201c{name}\u201d ({id})";

    private Guid UserId => _currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
