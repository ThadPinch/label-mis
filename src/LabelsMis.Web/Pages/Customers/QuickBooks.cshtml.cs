using LabelsMis.Domain.QuickBooks;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Customers;

/// <summary>Match customers: every active LabelShop customer with no QuickBooks link, the QuickBooks
/// customer with the same name where there is exactly one, and one click to link it.</summary>
[Authorize(Policy = MasterDataPolicies.QuickBooksLink)]
public class QuickBooksModel(QuickBooksCustomerService quickBooksCustomers) : PageModel
{
    private const string MessageKey = "QbMatchMessage";
    private const string ErrorKey = "QbMatchError";
    private const string DuplicateKey = "QbMatchDuplicate";

    public QuickBooksCustomerAvailability Availability { get; private set; } = null!;
    public QuickBooksCustomerMatchView? View { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    /// <summary>After Create in QuickBooks met a taken name: the row and the customer holding it.</summary>
    public (Guid CustomerId, string QuickBooksId, string Name)? Duplicate { get; private set; }

    /// <summary>Rows in the order the page shows them: exact matches, then the ones to pick, then the rest.</summary>
    public IReadOnlyList<QuickBooksCustomerMatch> Rows =>
        View?.Matches
            .OrderBy(m => m.Kind switch
            {
                QuickBooksMatchKind.Exact => 0,
                QuickBooksMatchKind.Ambiguous => 1,
                _ => 2
            })
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Message = TempData[MessageKey] as string;
        Error = TempData[ErrorKey] as string;
        Duplicate = ParseDuplicate(TempData[DuplicateKey] as string);
        Availability = await quickBooksCustomers.GetAvailabilityAsync(cancellationToken);
        if (!Availability.Connected)
        {
            return;
        }

        try
        {
            View = await quickBooksCustomers.GetMatchViewAsync(cancellationToken);
        }
        catch (Exception ex) when (QuickBooksCustomerPageErrors.Describe(ex) is { } message)
        {
            Error = message;
        }
    }

    public Task<IActionResult> OnPostLinkAsync(Guid customerId, string? qbId, CancellationToken cancellationToken) =>
        ActAsync(async () =>
            TempData[MessageKey] = QuickBooksCustomerPageErrors.Linked(
                await quickBooksCustomers.LinkAsync(customerId, qbId ?? string.Empty, cancellationToken)));

    public Task<IActionResult> OnPostLinkByNameAsync(Guid customerId, string? qbName, CancellationToken cancellationToken) =>
        ActAsync(async () =>
            TempData[MessageKey] = QuickBooksCustomerPageErrors.Linked(
                await quickBooksCustomers.LinkByNameAsync(customerId, qbName ?? string.Empty, cancellationToken)));

    public Task<IActionResult> OnPostCreateAsync(Guid customerId, CancellationToken cancellationToken) =>
        ActAsync(async () =>
        {
            var result = await quickBooksCustomers.CreateAsync(customerId, cancellationToken);
            if (result.Outcome == QuickBooksCustomerCreateOutcome.Created)
            {
                TempData[MessageKey] = $"Created \u201c{result.Customer!.DisplayName}\u201d in QuickBooks and linked it.";
            }
            else if (result.Customer is { } existing)
            {
                TempData[DuplicateKey] = $"{customerId:N}|{existing.Id}|{existing.DisplayName}";
                TempData[ErrorKey] = result.Message;
            }
            else
            {
                TempData[ErrorKey] = result.Message;
            }
        });

    /// <summary>Links every ticked exact match. Each box carries "customer id|QuickBooks id".</summary>
    public Task<IActionResult> OnPostLinkAllAsync(List<string>? selected, CancellationToken cancellationToken) =>
        ActAsync(async () =>
        {
            var pairs = (selected ?? [])
                .Select(s => s.Split('|', 2))
                .Where(p => p.Length == 2 && Guid.TryParse(p[0], out _) && !string.IsNullOrWhiteSpace(p[1]))
                .Select(p => new QuickBooksCustomerPair(Guid.Parse(p[0]), p[1]))
                .ToList();
            if (pairs.Count == 0)
            {
                TempData[ErrorKey] = "Tick at least one match to link.";
                return;
            }

            var result = await quickBooksCustomers.LinkAllAsync(pairs, cancellationToken);
            TempData[MessageKey] = result.Linked == 1 ? "Linked 1 customer." : $"Linked {result.Linked} customers.";
            if (result.Skipped.Count > 0)
            {
                TempData[ErrorKey] = "Not linked: " + string.Join(" ", result.Skipped);
            }
        });

    private async Task<IActionResult> ActAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (QuickBooksCustomerPageErrors.Describe(ex) is { } message)
        {
            TempData[ErrorKey] = message;
        }

        return RedirectToPage();
    }

    private static (Guid, string, string)? ParseDuplicate(string? value)
    {
        var parts = value?.Split('|', 3);
        return parts is { Length: 3 } && Guid.TryParse(parts[0], out var customerId)
            ? (customerId, parts[1], parts[2])
            : null;
    }
}
