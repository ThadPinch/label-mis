using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Customers;

[Authorize(Policy = MasterDataPolicies.Read)]
public class EditModel(
    CustomerService customerService,
    ICurrentUserService currentUser,
    UserManager<ApplicationUser> userManager,
    QuickBooksCustomerService quickBooksCustomers) : PageModel
{
    private const string QbMessageKey = "QbCustomerMessage";
    private const string QbErrorKey = "QbCustomerError";
    private const string QbDuplicateIdKey = "QbCustomerDuplicateId";
    private const string QbDuplicateNameKey = "QbCustomerDuplicateName";

    [BindProperty]
    public CustomerFormInput Input { get; set; } = new();

    public bool CanEdit => currentUser.CanEditMasterData;
    public bool IsActive { get; private set; } = true;

    public async Task<IActionResult> OnGetAsync(Guid id, string? qbSearch, CancellationToken cancellationToken)
    {
        var customer = await customerService.GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        Input = CustomerFormInput.FromEntity(customer);
        IsActive = customer.IsActive;
        ViewData["CanEditForm"] = CanEdit;
        await LoadSalesRepOptionsAsync(cancellationToken);
        await LoadQuickBooksBoxAsync(customer, qbSearch, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit)
        {
            return Forbid();
        }

        Input.NormalizeCollections();
        if (!ModelState.IsValid)
        {
            await ReloadAsync(id, cancellationToken);
            return Page();
        }

        try
        {
            // Compare against the stored markup so re-saving the displayed margin keeps it exact.
            var currentMarkup = await customerService.GetDefaultMarkupPctAsync(id, cancellationToken);
            await customerService.UpdateAsync(id, Input.ToForm(currentMarkup), cancellationToken);
            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadAsync(id, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit)
        {
            return Forbid();
        }

        await customerService.DeactivateAsync(id, cancellationToken);
        return this.RedirectToListPage();
    }

    public Task<IActionResult> OnPostQbLinkAsync(Guid id, string? qbId, CancellationToken cancellationToken) =>
        QuickBooksActionAsync(id, async () =>
            TempData[QbMessageKey] = QuickBooksCustomerPageErrors.Linked(
                await quickBooksCustomers.LinkAsync(id, qbId ?? string.Empty, cancellationToken)));

    public Task<IActionResult> OnPostQbUnlinkAsync(Guid id, CancellationToken cancellationToken) =>
        QuickBooksActionAsync(id, async () =>
        {
            await quickBooksCustomers.UnlinkAsync(id, cancellationToken);
            TempData[QbMessageKey] = "Unlinked from QuickBooks. Nothing was changed in QuickBooks.";
        });

    public Task<IActionResult> OnPostQbCreateAsync(Guid id, CancellationToken cancellationToken) =>
        QuickBooksActionAsync(id, async () =>
        {
            var result = await quickBooksCustomers.CreateAsync(id, cancellationToken);
            if (result.Outcome == QuickBooksCustomerCreateOutcome.Created)
            {
                TempData[QbMessageKey] = $"Created \u201c{result.Customer!.DisplayName}\u201d in QuickBooks and linked it.";
                return;
            }

            if (result.Customer is { } existing)
            {
                TempData[QbDuplicateIdKey] = existing.Id;
                TempData[QbDuplicateNameKey] = existing.DisplayName;
            }
            else
            {
                TempData[QbErrorKey] = result.Message;
            }
        });

    private async Task<IActionResult> QuickBooksActionAsync(Guid id, Func<Task> action)
    {
        if (!CanEdit)
        {
            return Forbid();
        }

        try
        {
            await action();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (QuickBooksCustomerPageErrors.Describe(ex) is { } message)
        {
            TempData[QbErrorKey] = message;
        }

        return RedirectToPage(null, null, new { id }, "quickbooks");
    }

    private async Task LoadQuickBooksBoxAsync(Domain.Entities.Customer customer, string? qbSearch, CancellationToken cancellationToken)
    {
        var availability = await quickBooksCustomers.GetAvailabilityAsync(cancellationToken);
        var error = TempData[QbErrorKey] as string;
        IReadOnlyList<Domain.QuickBooks.QuickBooksCustomerSummary>? results = null;
        var searchText = string.IsNullOrWhiteSpace(qbSearch) ? null : qbSearch.Trim();
        if (searchText is not null && availability.Connected && CanEdit)
        {
            try
            {
                results = await quickBooksCustomers.SearchAsync(searchText, customer.Id, cancellationToken);
            }
            catch (Exception ex) when (QuickBooksCustomerPageErrors.Describe(ex) is { } message)
            {
                error = message;
            }
        }

        ViewData["QuickBooksBox"] = new QuickBooksCustomerBox(
            customer.Id,
            customer.QuickBooksCustomerId,
            customer.QuickBooksCustomerName,
            availability,
            CanEdit,
            searchText,
            results,
            TempData[QbMessageKey] as string,
            error,
            TempData[QbDuplicateIdKey] as string,
            TempData[QbDuplicateNameKey] as string);
    }

    private async Task ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await customerService.GetByIdAsync(id, cancellationToken);
        IsActive = customer?.IsActive ?? false;
        ViewData["CanEditForm"] = CanEdit;
        await LoadSalesRepOptionsAsync(cancellationToken);
        if (customer is not null)
        {
            await LoadQuickBooksBoxAsync(customer, null, cancellationToken);
        }
    }

    private async Task LoadSalesRepOptionsAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        ViewData["SalesRepOptions"] = users.Select(u => new SelectListItem(
            u.Email ?? u.UserName ?? u.Id.ToString(),
            u.Id.ToString())).ToList();
    }
}
