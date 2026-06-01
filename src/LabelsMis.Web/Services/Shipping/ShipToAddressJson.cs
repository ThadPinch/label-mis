using LabelsMis.Web.Services.Customers;
using Microsoft.AspNetCore.Mvc;

namespace LabelsMis.Web.Services.Shipping;

/// <summary>
/// Builds the JSON payload of a customer's addresses used by the ship-to picker on
/// estimates and sales orders, so both endpoints stay in sync.
/// </summary>
public static class ShipToAddressJson
{
    public static async Task<IActionResult> BuildAsync(
        CustomerService customerService, Guid? customerId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id || id == Guid.Empty)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var options = await customerService.GetAddressOptionsAsync(id, cancellationToken);
        return new JsonResult(options.Select(a => new
        {
            id = a.Id,
            type = a.AddressType.ToString(),
            recipientName = (string?)null,
            street1 = a.Street1,
            street2 = a.Street2,
            city = a.City,
            state = a.State,
            zip = a.Zip,
            country = a.Country,
            label = $"{a.AddressType}: {a.Street1}, {a.City} {a.State} {a.Zip}".Trim()
        }));
    }
}
