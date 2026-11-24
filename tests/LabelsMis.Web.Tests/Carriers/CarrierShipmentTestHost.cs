using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Storage;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Carriers;
using LabelsMis.Web.Services.Email;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Pdfs;
using LabelsMis.Web.Services.Settings;
using LabelsMis.Web.Services.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests.Carriers;

/// <summary>File storage that lives in memory, so tests can see what was kept.</summary>
public sealed class InMemoryFileStorage : IFileStorageClient
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Stands in for Spaces being down.</summary>
    public bool FailUploads { get; set; }

    public async Task<StoredFile> UploadAsync(
        string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (FailUploads)
        {
            throw new IOException("Storage is down.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[key] = buffer.ToArray();
        return new StoredFile(key, contentType, buffer.Length);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Files.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException("Not stored.", key);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.ContainsKey(key));

    public Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredObjectInfo>>(Files
            .Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(f => new StoredObjectInfo(f.Key, DateTime.UtcNow, f.Value.Length))
            .ToList());
}

/// <summary>An order made for a test: one line per quantity, each with a job at Rewound (ready to ship).</summary>
public sealed record SeededOrder(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    IReadOnlyList<Guid> LineIds,
    IReadOnlyList<Guid> JobIds,
    IReadOnlyList<int> Quantities)
{
    /// <summary>Every line in full, as Record shipment would send it.</summary>
    public List<ManualShipmentLineInput> ShipLines() =>
        LineIds.Select((lineId, i) => new ManualShipmentLineInput(lineId, JobIds[i], Quantities[i])).ToList();
}

/// <summary><see cref="CarrierTestHost"/> plus everything a shipment touches: the real shipment
/// and invoice services over the test database, and file storage in memory.</summary>
public abstract class CarrierShipmentTestHost : CarrierTestHost
{
    protected static readonly ShippingAddress CustomerAddress =
        new("Desert Bloom Coffee", "2200 E Camelback Rd", null, "Phoenix", "AZ", "85016", "US");

    protected InMemoryFileStorage Storage { get; } = new();

    protected override void AddServices(IServiceCollection services)
    {
        services.AddSingleton<IFileStorageClient>(Storage);
        services.AddSingleton(Options.Create(new InvoiceOptions()));
        services.AddScoped<IEmailSender, NoopEmailSender>();
        services.AddScoped<DocumentNumberService>();
        services.AddScoped<GeneralSettingsService>();
        services.AddScoped<DocumentEmailService>();
        services.AddScoped<TempPdfStorage>();
        services.AddScoped<InvoicePdfGenerator>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<ShipmentService>();
        services.AddScoped<CarrierShipmentService>();
    }

    protected static LabelBoxInput Box(decimal weightLb, decimal declaredValue = 0m) => new(weightLb, 12m, 10m, 8m, declaredValue);

    /// <summary>The shipment as Record shipment hands it over: to the customer, today.</summary>
    protected static LabelRequestInput Request(params LabelBoxInput[] boxes) =>
        new(new DateOnly(2026, 11, 24), CustomerAddress, "Pat Receiver", "480-555-0142", false, boxes);

    protected Task<T> WithLabelsAsync<T>(Func<CarrierShipmentService, Task<T>> action) =>
        InRequestAsync(services => action(services.GetRequiredService<CarrierShipmentService>()));

    protected Task<T> WithShipmentsAsync<T>(Func<ShipmentService, Task<T>> action) =>
        InRequestAsync(services => action(services.GetRequiredService<ShipmentService>()));

    protected Task<T> WithInvoicesAsync<T>(Func<InvoiceService, Task<T>> action) =>
        InRequestAsync(services => action(services.GetRequiredService<InvoiceService>()));

    protected Task<LabelQuote> RatesAsync(SeededOrder order, params LabelBoxInput[] boxes) =>
        WithLabelsAsync(labels => labels.GetRatesAsync(order.OrderId, Request(boxes)));

    protected Task<BuyLabelResult> BuyAsync(SeededOrder order, LabelQuote quote, string serviceCode, params LabelBoxInput[] boxes) =>
        WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(boxes), order.ShipLines(), quote.Token,
            quote.Rates.Single(r => r.ServiceCode == serviceCode).OfferId));

    /// <summary>Rates then Buy for one service: the whole of Record shipment's label path.</summary>
    protected async Task<BuyLabelResult> BuyAsync(SeededOrder order, string serviceCode, params LabelBoxInput[] boxes) =>
        await BuyAsync(order, await RatesAsync(order, boxes), serviceCode, boxes);

    protected Task<Shipment?> ReadShipmentAsync(Guid shipmentId) =>
        InDbAsync(db => db.Shipments.AsNoTracking()
            .Include(s => s.Packages)
            .Include(s => s.Lines)
            .SingleOrDefaultAsync(s => s.Id == shipmentId));

    protected Task<JobStatus> JobStatusAsync(Guid jobId) =>
        InDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.Status).SingleAsync());

    protected Task<SalesOrderStatus> OrderStatusAsync(SeededOrder order) =>
        InDbAsync(db => db.SalesOrders.AsNoTracking().Where(o => o.Id == order.OrderId).Select(o => o.Status).SingleAsync());

    protected Task<decimal> CarrierMultiplierAsync() =>
        InDbAsync(db => db.GeneralSettings.AsNoTracking().Select(g => g.CarrierCostMultiplier).SingleAsync());

    /// <summary>A set-up customer (tax form back, a billing contact with a phone) with an order of
    /// one line per quantity on a method of the given type (null = no method), shipping to the
    /// customer's address, each line with a job at Rewound.</summary>
    protected async Task<SeededOrder> SeedOrderAsync(ShippingMethodType? methodType, params int[] quantities)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineIds = new List<Guid>();
        var jobIds = new List<Guid>();

        await InDbAsync(async db =>
        {
            Guid? methodId = null;
            if (methodType is { } type)
            {
                methodId = Guid.NewGuid();
                db.ShippingMethods.Add(ShippingMethod.Create(methodId.Value, $"Label {type} {suffix}", type,
                    type == ShippingMethodType.Carrier ? 35m : 0m, type != ShippingMethodType.Pickup, TestUserId, now));
            }

            db.Suppliers.Add(Supplier.Create(supplierId, $"Label Sup {suffix}", $"LS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
            var customer = Customer.Create(customerId, $"Label Customer {suffix}", $"LC{suffix}"[..10],
                PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, TestUserId, now);
            customer.SetQuickBooksSetup($"QB-{suffix}", true, null, TestUserId, now);
            db.Customers.Add(customer);
            db.Contacts.Add(Contact.Create(Guid.NewGuid(), customerId, "Bea", "Biller", $"ap-{suffix}@example.com",
                "480-555-0142", "Billing", true, true, TestUserId, now));
            db.Stocks.Add(Stock.Create(stockId, $"LT{suffix}"[..10], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
                supplierId, null, 0.85m, 1000m, TestUserId, now));
            var product = Product.Create(
                Guid.NewGuid(), customerId, [customerId], $"LBL-{suffix}", null, "Label test labels", null,
                4, 3, 0.125m, stockId, InkSet.CMYK, "[]", null, null, null, TestUserId, now);
            db.Products.Add(product);
            foreach (var assignment in product.CustomerAssignments)
            {
                db.ProductCustomers.Add(assignment);
            }

            var order = SalesOrder.CreateOpen(
                orderId, $"SO-L-{suffix}", customerId, null, null, $"PO {suffix}", now, null, null, null,
                methodId, 0m, CustomerAddress, TestUserId, now);
            db.SalesOrders.Add(order);
            for (var i = 0; i < quantities.Length; i++)
            {
                var line = SalesOrderLine.Create(
                    Guid.NewGuid(), order.Id, i + 1, product.Id, $"Label test labels {i + 1}", null, quantities[i], 0.5m, null,
                    null, TestUserId, now);
                order.AddLine(line);
                db.SalesOrderLines.Add(line);
                var job = Job.CreatePlanned(Guid.NewGuid(), $"JOB-L-{suffix}-{i + 1}", line.Id, product.Id,
                    quantities[i], quantities[i], null, 0, null, null, TestUserId, now);
                job.SetStatus(JobStatus.Rewound, TestUserId, now);
                db.Jobs.Add(job);
                lineIds.Add(line.Id);
                jobIds.Add(job.Id);
            }

            await db.SaveChangesAsync();
        });

        return new SeededOrder(orderId, $"SO-L-{suffix}", customerId, lineIds, jobIds, quantities);
    }

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
}
