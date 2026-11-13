using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Invoices;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

public record QbAddress(string? Name, string? Line1, string? Line2, string? City, string? State, string? Zip, string? Country)
{
    public static QbAddress? From(ShippingAddress address) =>
        address.HasAddress
            ? new QbAddress(address.RecipientName, address.Street1, address.Street2, address.City, address.State, address.Zip, address.Country)
            : null;
}

/// <summary>One QuickBooks line exactly as it is sent: quantity is the quantity <em>ordered</em>;
/// <see cref="Amount"/> is what LabelShop bills for the line (its total in cents, the same as
/// <see cref="LabelShopAmount"/>); the rate goes with it only when <see cref="SendsRate"/>.</summary>
public record QbPreviewLine(
    Guid InvoiceLineId,
    int LineNumber,
    bool IsProductLine,
    string LineType,
    string Item,
    string Class,
    string ItemSource,
    string ClassSource,
    string? ItemOverride,
    string? ClassOverride,
    string Description,
    int Quantity,
    decimal Rate,
    decimal Amount,
    decimal LabelShopAmount)
{
    /// <summary>Quantity × rate is the amount, so QuickBooks gets the rate too; otherwise it gets the
    /// quantity and the amount with no rate (same rule as the payload).</summary>
    public bool SendsRate => LabelsMis.Domain.QuickBooks.QuickBooksInvoicePayloadBuilder.SendsRate(Quantity, Rate, Amount);
}

public record QbPreviewShipping(string Item, string Class, decimal Amount);

/// <summary>
/// One invoice as QuickBooks Online will receive it (E9). DocNumber = the LabelShop invoice
/// number. Tax is not part of it: QuickBooks applies the customer's tax set-up to the ship-to, so
/// <see cref="Total"/> is lines + shipping and LabelShop's own tax is shown for reference only.
/// </summary>
public record QbInvoicePreview(
    Guid InvoiceId,
    string DocNumber,
    string Status,
    DateOnly TxnDate,
    DateOnly DueDate,
    string Terms,
    Guid CustomerId,
    string CustomerName,
    string? QuickBooksCustomerId,
    string? CustomerPoNumber,
    Guid SalesOrderId,
    string OrderNumber,
    string? BillingEmail,
    QbAddress? ShipTo,
    QbAddress? BillTo,
    IReadOnlyList<QbPreviewLine> Lines,
    QbPreviewShipping Shipping,
    decimal LinesTotal,
    decimal Total,
    decimal LabelShopSubtotal,
    decimal LabelShopTax,
    decimal LabelShopTotal,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    DateTime? ExportedAt,
    string? ExportedBy,
    IReadOnlyList<string> ItemOptions,
    IReadOnlyList<string> ClassOptions,
    /// <summary>No contact is ticked Billing, so <see cref="BillingEmail"/> is the primary contact's.</summary>
    bool BillingEmailIsPrimaryFallback = false)
{
    public bool IsReady => Status == QbInvoiceStatus.Ready;
    public bool CanEditOverrides => Status is QbInvoiceStatus.Ready or QbInvoiceStatus.Waiting;
}

/// <summary>Phase 0 states, mapping onto Phase 1's ExportStatus (Ready / Blocked / Exported / Closed).</summary>
public static class QbInvoiceStatus
{
    public const string Ready = "Ready";
    public const string Waiting = "Waiting";
    public const string Exported = "Exported";
    public const string NonBillable = "Non-billable";
    public const string Void = "Void";
}

/// <summary>Builds the QuickBooks-shaped view of an invoice (preview page and JSON endpoint).</summary>
public class QuickBooksInvoiceService(
    LabelsMisDbContext db,
    InvoiceService invoiceService,
    QuickBooksMappingService mappingService,
    ICurrentUserService currentUser)
{
    /// <summary>Sets (blank clears) one line's QuickBooks item / class override from the preview.
    /// Refused once the invoice is exported, non-billable or void.</summary>
    public async Task<InvoiceLineQbMapping> SetLineOverridesAsync(
        Guid invoiceId,
        Guid invoiceLineId,
        string? item,
        string? qbClass,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
        var invoice = await db.Invoices.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => new { i.Status, i.NonBillable, i.QbExportedAt })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status == InvoiceStatus.Void || invoice.NonBillable || invoice.QbExportedAt is not null)
        {
            throw new InvalidOperationException("Only invoices waiting for QuickBooks can be changed.");
        }

        var line = await db.InvoiceLines.SingleOrDefaultAsync(l => l.Id == invoiceLineId && l.InvoiceId == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice line not found.");
        line.SetQbOverrides(item, qbClass, userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        var mapped = await mappingService.MapInvoiceAsync(invoiceId, cancellationToken);
        return mapped!.Lines.Single(l => l.InvoiceLineId == invoiceLineId);
    }

    public async Task<QbInvoicePreview?> GetPreviewAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Customer).ThenInclude(c => c.Addresses)
            .Include(i => i.Customer).ThenInclude(c => c.Contacts)
            .Include(i => i.SalesOrder)
            .Include(i => i.Lines)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var mapped = (await mappingService.MapInvoiceAsync(invoiceId, cancellationToken))!;
        var orderLineIds = invoice.Lines.Where(l => l.SalesOrderLineId.HasValue).Select(l => l.SalesOrderLineId!.Value).ToList();
        var orderedQty = await db.SalesOrderLines.AsNoTracking()
            .Where(l => orderLineIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Quantity, cancellationToken);

        var warnings = new List<string>();
        var lines = new List<QbPreviewLine>();
        foreach (var line in invoice.Lines.OrderBy(l => l.LineNumber))
        {
            var mapping = mapped.Lines.Single(m => m.InvoiceLineId == line.Id);
            var quantity = line.SalesOrderLineId is Guid orderLineId && orderedQty.TryGetValue(orderLineId, out var ordered)
                ? ordered
                : line.Quantity;
            // The amount is always what LabelShop bills (the line total in cents), so the QuickBooks
            // invoice matches LabelShop's subtotal; this is what Send to QuickBooks sends. An invoice
            // that bills a different quantity than was ordered (one generated from a shipment) keeps
            // the ordered quantity beside the billed amount and goes without a rate.
            var amount = Math.Round(line.LineTotal, 2, MidpointRounding.AwayFromZero);
            if (quantity != line.Quantity)
            {
                warnings.Add($"Line {line.LineNumber}: QuickBooks gets the ordered quantity ({quantity:N0}) with the amount this invoice bills for {line.Quantity:N0} ({amount:C2}), and no rate.");
            }
            else if (!LabelsMis.Domain.QuickBooks.QuickBooksInvoicePayloadBuilder.SendsRate(quantity, line.UnitPrice, amount))
            {
                warnings.Add($"Line {line.LineNumber}: {quantity:N0} × {line.UnitPrice:0.00####} = {quantity * line.UnitPrice:C2}, but LabelShop bills {amount:C2} — QuickBooks gets the amount, not the rate (key the amount, not the rate, if keying by hand).");
            }

            if (!mapping.IsProductLine && mapping.Mapping.ItemSource.Contains("no keyword", StringComparison.Ordinal))
            {
                warnings.Add($"Line {line.LineNumber}: no charge keyword matched \"{line.Description}\" — check the item.");
            }

            lines.Add(new QbPreviewLine(
                line.Id,
                line.LineNumber,
                mapping.IsProductLine,
                mapping.Mapping.LineType.ToString(),
                mapping.Mapping.Item,
                mapping.Mapping.Class,
                mapping.Mapping.ItemSource,
                mapping.Mapping.ClassSource,
                line.QbItemOverride,
                line.QbClassOverride,
                line.Description,
                quantity,
                line.UnitPrice,
                amount,
                line.LineTotal));
        }

        var linesTotal = lines.Sum(l => l.Amount);
        var shipping = Math.Round(invoice.ShippingAmount, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(linesTotal - invoice.Subtotal) >= 0.01m)
        {
            warnings.Add($"QuickBooks lines add up to {linesTotal:C2}; the LabelShop subtotal is {invoice.Subtotal:C2}.");
        }

        if (string.IsNullOrWhiteSpace(invoice.Customer.QuickBooksCustomerId))
        {
            warnings.Add($"Not linked to a QuickBooks customer yet — find \"{invoice.Customer.Name}\" in QuickBooks and add its id on the customer.");
        }

        var billingEmail = CustomerBillingRules.ChooseBillingEmail(
            invoice.Customer.Contacts.Select(c => new BillingContactFacts(c.IsBillingContact, c.Email, c.IsPrimary)));
        if (billingEmail is { IsPrimaryFallback: true })
        {
            warnings.Add($"No contact is ticked Billing — using the primary contact's email ({billingEmail.Email}). "
                + "Tick Billing on the customer's accounts-payable contact if invoices go to someone else.");
        }

        var status = invoice.Status == InvoiceStatus.Void
            ? QbInvoiceStatus.Void
            : invoice.NonBillable
                ? QbInvoiceStatus.NonBillable
                : invoice.QbExportedAt is not null
                    ? QbInvoiceStatus.Exported
                    : QbInvoiceStatus.Waiting;
        IReadOnlyList<string> blockers = status switch
        {
            QbInvoiceStatus.Void => ["Void — nothing goes to QuickBooks"],
            QbInvoiceStatus.NonBillable => [$"Non-billable — closed without a QuickBooks record ({invoice.NonBillableReason})"],
            QbInvoiceStatus.Exported => [],
            _ => await invoiceService.GetWaitingReasonsAsync(invoiceId, cancellationToken)
        };
        if (status == QbInvoiceStatus.Waiting && blockers.Count == 0)
        {
            status = QbInvoiceStatus.Ready;
        }

        string? exportedBy = null;
        if (invoice.QbExportedById is Guid exporterId)
        {
            exportedBy = await db.Users.AsNoTracking()
                .Where(u => u.Id == exporterId)
                .Select(u => u.Email ?? u.UserName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var customer = invoice.Customer;
        var billingAddress = customer.Addresses
            .Where(a => a.AddressType == AddressType.Billing)
            .OrderByDescending(a => a.IsDefault)
            .Select(a => new QbAddress(customer.Name, a.Street1, a.Street2, a.City, a.State, a.Zip, a.Country))
            .FirstOrDefault();
        var shipTo = QbAddress.From(invoice.SalesOrder.ShippingAddress)
            ?? customer.Addresses
                .Where(a => a.AddressType == AddressType.Shipping)
                .OrderByDescending(a => a.IsDefault)
                .Select(a => new QbAddress(customer.Name, a.Street1, a.Street2, a.City, a.State, a.Zip, a.Country))
                .FirstOrDefault()
            ?? billingAddress;

        var options = await mappingService.GetLineOptionsAsync(mapped.Mapping, cancellationToken);
        return new QbInvoicePreview(
            invoice.Id,
            invoice.InvoiceNumber,
            status,
            invoice.InvoiceDate,
            invoice.DueDate,
            customer.Terms.Label(),
            customer.Id,
            customer.Name,
            customer.QuickBooksCustomerId,
            invoice.SalesOrder.CustomerPoNumber,
            invoice.SalesOrderId,
            invoice.SalesOrder.OrderNumber,
            billingEmail?.Email,
            shipTo,
            billingAddress,
            lines,
            new QbPreviewShipping(mapped.Shipping.Item, mapped.Shipping.Class, shipping),
            linesTotal,
            linesTotal + shipping,
            invoice.Subtotal,
            invoice.TaxAmount,
            invoice.Total,
            blockers,
            warnings,
            invoice.QbExportedAt,
            exportedBy,
            options.Items,
            options.Classes,
            billingEmail?.IsPrimaryFallback ?? false);
    }
}
