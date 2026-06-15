using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Infrastructure.Persistence;

public class LabelsMisDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public LabelsMisDbContext(DbContextOptions<LabelsMisDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<StockCostHistory> StockCostHistory => Set<StockCostHistory>();
    public DbSet<Die> Dies => Set<Die>();
    public DbSet<DieUsage> DieUsage => Set<DieUsage>();
    public DbSet<Ink> Inks => Set<Ink>();
    public DbSet<FinishingOperation> FinishingOperations => Set<FinishingOperation>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<Press> Presses => Set<Press>();
    public DbSet<Estimate> Estimates => Set<Estimate>();
    public DbSet<EstimateLine> EstimateLines => Set<EstimateLine>();
    public DbSet<EstimateQuantityBreak> EstimateQuantityBreaks => Set<EstimateQuantityBreak>();
    public DbSet<EstimateRevision> EstimateRevisions => Set<EstimateRevision>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCustomer> ProductCustomers => Set<ProductCustomer>();
    public DbSet<RollSpec> RollSpecs => Set<RollSpec>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobOperation> JobOperations => Set<JobOperation>();
    public DbSet<JobTimeEntry> JobTimeEntries => Set<JobTimeEntry>();
    public DbSet<JobMaterialUsage> JobMaterialUsages => Set<JobMaterialUsage>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Roll> Rolls => Set<Roll>();
    public DbSet<RollMovement> RollMovements => Set<RollMovement>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentPackage> ShipmentPackages => Set<ShipmentPackage>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<TrackingEvent> TrackingEvents => Set<TrackingEvent>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StorageSettings> StorageSettings => Set<StorageSettings>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<GeneralSettings> GeneralSettings => Set<GeneralSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("public");
        builder.ApplyConfigurationsFromAssembly(typeof(LabelsMisDbContext).Assembly);
    }
}
