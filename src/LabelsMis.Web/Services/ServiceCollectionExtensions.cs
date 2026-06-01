using LabelsMis.Domain.Estimating;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Background;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.Dies;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.FinishingOperations;
using LabelsMis.Web.Services.Inks;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Presses;
using LabelsMis.Web.Services.Products;
using LabelsMis.Web.Services.PurchaseOrders;
using LabelsMis.Web.Services.Rolls;
using LabelsMis.Web.Services.SalesOrders;
using LabelsMis.Web.Services.Shipments;
using LabelsMis.Web.Services.Shipping;
using LabelsMis.Web.Services.Stocks;
using LabelsMis.Web.Services.Suppliers;
using LabelsMis.Web.Services.Artwork;
using LabelsMis.Web.Services.Settings;
using LabelsMis.Web.Services.Users;

namespace LabelsMis.Web.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<StockService>();
        services.AddScoped<DieService>();
        services.AddScoped<InkService>();
        services.AddScoped<FinishingOperationService>();
        services.AddScoped<ShippingMethodService>();
        services.AddScoped<PressService>();
        services.AddScoped<DocumentNumberService>();
        services.AddScoped<EstimateCalculationMapper>();
        services.AddSingleton<EstimatingService>();
        services.AddScoped<EstimateService>();
        services.AddScoped<EstimatePdfGenerator>();
        services.AddScoped<ProductService>();
        services.AddScoped<SalesOrderService>();
        services.AddScoped<JobService>();
        services.AddScoped<JobTicketPdfGenerator>();
        services.AddScoped<PurchaseOrderService>();
        services.AddScoped<RollService>();
        services.AddScoped<ShipmentService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<StorageSettingsService>();
        services.AddScoped<ArtworkService>();
        services.AddHostedService<ShipmentTrackingPoller>();
        return services;
    }

    public static IServiceCollection AddMasterDataAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(MasterDataPolicies.Read, policy =>
                policy.RequireRole(MasterDataPolicies.ReadRoles))
            .AddPolicy(MasterDataPolicies.Edit, policy =>
                policy.RequireRole(MasterDataPolicies.EditRoles))
            .AddPolicy(TransactionPolicies.EstimatesRead, policy =>
                policy.RequireRole(TransactionPolicies.EstimatesReadRoles))
            .AddPolicy(TransactionPolicies.EstimatesEdit, policy =>
                policy.RequireRole(TransactionPolicies.EstimatesEditRoles))
            .AddPolicy(TransactionPolicies.EstimatesAdmin, policy =>
                policy.RequireRole(TransactionPolicies.EstimatesAdminRoles))
            .AddPolicy(TransactionPolicies.ProductsRead, policy =>
                policy.RequireRole(TransactionPolicies.ProductsReadRoles))
            .AddPolicy(TransactionPolicies.ProductsEdit, policy =>
                policy.RequireRole(TransactionPolicies.ProductsEditRoles))
            .AddPolicy(TransactionPolicies.SalesOrdersRead, policy =>
                policy.RequireRole(TransactionPolicies.SalesOrdersReadRoles))
            .AddPolicy(TransactionPolicies.SalesOrdersEdit, policy =>
                policy.RequireRole(TransactionPolicies.SalesOrdersEditRoles))
            .AddPolicy(TransactionPolicies.JobsRead, policy =>
                policy.RequireRole(TransactionPolicies.JobsReadRoles))
            .AddPolicy(TransactionPolicies.JobsEdit, policy =>
                policy.RequireRole(TransactionPolicies.JobsEditRoles))
            .AddPolicy(TransactionPolicies.JobsOperator, policy =>
                policy.RequireRole(TransactionPolicies.JobsOperatorRoles))
            .AddPolicy(TransactionPolicies.JobsAdmin, policy =>
                policy.RequireRole(TransactionPolicies.JobsAdminRoles))
            .AddPolicy(TransactionPolicies.InventoryRead, policy =>
                policy.RequireRole(TransactionPolicies.InventoryReadRoles))
            .AddPolicy(TransactionPolicies.InventoryEdit, policy =>
                policy.RequireRole(TransactionPolicies.InventoryEditRoles))
            .AddPolicy(TransactionPolicies.ShippingRead, policy =>
                policy.RequireRole(TransactionPolicies.ShippingReadRoles))
            .AddPolicy(TransactionPolicies.ShippingEdit, policy =>
                policy.RequireRole(TransactionPolicies.ShippingEditRoles))
            .AddPolicy(TransactionPolicies.InvoicesRead, policy =>
                policy.RequireRole(TransactionPolicies.InvoicesReadRoles))
            .AddPolicy(TransactionPolicies.InvoicesEdit, policy =>
                policy.RequireRole(TransactionPolicies.InvoicesEditRoles))
            .AddPolicy(TransactionPolicies.AdminOverride, policy =>
                policy.RequireRole(TransactionPolicies.AdminOverrideRoles));
        return services;
    }
}
