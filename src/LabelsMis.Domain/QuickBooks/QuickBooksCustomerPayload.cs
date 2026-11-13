using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>The body of a QuickBooks customer create, and the <c>requestid</c> that makes a repeat
/// of the same create (a double click) return the first customer instead of making a second.</summary>
public sealed record QuickBooksCustomerCreate(string Json, string RequestId, string DisplayName);

/// <summary>Builds the QuickBooks customer for a LabelShop customer: display and company name,
/// the billing email (<see cref="CustomerBillingRules.ChooseBillingEmail"/>), a phone, and the
/// billing and shipping addresses. Terms and tax status are left to QuickBooks: the invoice carries
/// its own terms, and tax is set up there by hand from the new-client form.</summary>
public static class QuickBooksCustomerPayload
{
    /// <summary>Length of the <c>requestid</c>: hex characters of a SHA-256, well inside Intuit's limit.</summary>
    public const int RequestIdLength = 32;

    public static QuickBooksCustomerCreate Build(Customer customer)
    {
        var displayName = DisplayNameFor(customer.Name);
        var body = new JsonObject
        {
            ["DisplayName"] = displayName,
            ["CompanyName"] = Cut(customer.Name.Trim(), 100)
        };

        var contacts = customer.Contacts.ToList();
        var email = CustomerBillingRules.ChooseBillingEmail(
            contacts.Select(c => new BillingContactFacts(c.IsBillingContact, c.Email, c.IsPrimary)))?.Email;
        if (email is { Length: <= 100 })
        {
            body["PrimaryEmailAddr"] = new JsonObject { ["Address"] = email };
        }

        var phone = contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Phone))
            .OrderByDescending(c => c.IsBillingContact)
            .ThenByDescending(c => c.IsPrimary)
            .Select(c => c.Phone!.Trim())
            .FirstOrDefault();
        if (phone is { Length: <= 30 })
        {
            body["PrimaryPhone"] = new JsonObject { ["FreeFormNumber"] = phone };
        }

        var billing = Pick(customer.Addresses, AddressType.Billing);
        var shipping = Pick(customer.Addresses, AddressType.Shipping);
        if ((billing ?? shipping) is { } bill)
        {
            body["BillAddr"] = Address(bill);
        }

        if ((shipping ?? billing) is { } ship)
        {
            body["ShipAddr"] = Address(ship);
        }

        var json = body.ToJsonString();
        return new QuickBooksCustomerCreate(json, RequestId(customer.Id, json), displayName);
    }

    /// <summary>The customer's name as QuickBooks accepts a display name: no colon (QuickBooks uses
    /// it between a parent and a sub-customer), no tabs or line breaks, at most 500 characters.</summary>
    public static string DisplayNameFor(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(ch switch
            {
                ':' => " - ",
                _ when char.IsControl(ch) => " ",
                _ => ch.ToString()
            });
        }

        var collapsed = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Cut(collapsed, 500);
    }

    /// <summary>Hex SHA-256 of the customer id and the body: the same create sent twice carries the
    /// same id, and a changed body (an edited name or address) gets a new one.</summary>
    public static string RequestId(Guid customerId, string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"customer:{customerId:N}\n{json}"));
        return Convert.ToHexString(hash).ToLowerInvariant()[..RequestIdLength];
    }

    private static Address? Pick(IEnumerable<Address> addresses, AddressType type) =>
        addresses.Where(a => a.AddressType == type)
            .OrderByDescending(a => a.IsDefault)
            .FirstOrDefault();

    private static JsonObject Address(Address address)
    {
        var node = new JsonObject { ["Line1"] = Cut(address.Street1, 500) };
        if (!string.IsNullOrWhiteSpace(address.Street2))
        {
            node["Line2"] = Cut(address.Street2, 500);
        }

        node["City"] = Cut(address.City, 255);
        node["CountrySubDivisionCode"] = Cut(address.State, 255);
        node["PostalCode"] = Cut(address.Zip, 30);
        node["Country"] = Cut(address.Country, 255);
        return node;
    }

    private static string Cut(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max].TrimEnd();
    }
}
