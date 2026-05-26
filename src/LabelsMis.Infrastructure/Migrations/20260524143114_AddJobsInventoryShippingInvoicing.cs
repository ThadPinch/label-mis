using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobsInventoryShippingInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Job",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityOrdered = table.Column<int>(type: "integer", nullable: false),
                    QuantityPlanned = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScheduledForDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ScheduledPressId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Job", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Job_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Job_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Job_Press_ScheduledPressId",
                        column: x => x.ScheduledPressId,
                        principalSchema: "public",
                        principalTable: "Press",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Job_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "public",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Job_SalesOrderLine_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalSchema: "public",
                        principalTable: "SalesOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrder",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpectedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "public",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shipment",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Carrier = table.Column<int>(type: "integer", nullable: false),
                    ServiceLevel = table.Column<int>(type: "integer", nullable: false),
                    ShipFromAddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipToAddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalDeclaredValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BillingType = table.Column<int>(type: "integer", nullable: false),
                    BillingAccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TotalShippingCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipment_Address_ShipFromAddressId",
                        column: x => x.ShipFromAddressId,
                        principalSchema: "public",
                        principalTable: "Address",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipment_Address_ShipToAddressId",
                        column: x => x.ShipToAddressId,
                        principalSchema: "public",
                        principalTable: "Address",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipment_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipment_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipment_SalesOrder_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalSchema: "public",
                        principalTable: "SalesOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLine",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    StockId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityReceivedLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_PurchaseOrder_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "public",
                        principalTable: "PurchaseOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_Stock_StockId",
                        column: x => x.StockId,
                        principalSchema: "public",
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invoice",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ShippingAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BalanceDue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QbExportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QbInvoiceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PdfFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoice_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoice_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoice_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoice_SalesOrder_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalSchema: "public",
                        principalTable: "SalesOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoice_Shipment_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "public",
                        principalTable: "Shipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentLine",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityShipped = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentLine_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLine_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLine_Job_JobId",
                        column: x => x.JobId,
                        principalSchema: "public",
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShipmentLine_SalesOrderLine_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalSchema: "public",
                        principalTable: "SalesOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLine_Shipment_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "public",
                        principalTable: "Shipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentPackage",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageNumber = table.Column<int>(type: "integer", nullable: false),
                    WeightLb = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    LengthIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    WidthIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    HeightIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LabelUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeclaredValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ShippingCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPackage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentPackage_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackage_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackage_Shipment_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "public",
                        principalTable: "Shipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Receipt",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QuantityLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipt_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipt_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipt_PurchaseOrderLine_PoLineId",
                        column: x => x.PoLineId,
                        principalSchema: "public",
                        principalTable: "PurchaseOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLine",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLine_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLine_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLine_Invoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "public",
                        principalTable: "Invoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceLine_Job_JobId",
                        column: x => x.JobId,
                        principalSchema: "public",
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InvoiceLine_SalesOrderLine_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalSchema: "public",
                        principalTable: "SalesOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Payment",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payment_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payment_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payment_Invoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "public",
                        principalTable: "Invoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackingEvent",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentPackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StatusDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackingEvent_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackingEvent_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackingEvent_ShipmentPackage_ShipmentPackageId",
                        column: x => x.ShipmentPackageId,
                        principalSchema: "public",
                        principalTable: "ShipmentPackage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Roll",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RollBarcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WidthIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    OriginalLengthLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    RemainingLengthLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roll", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roll_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Roll_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Roll_Receipt_ReceiptId",
                        column: x => x.ReceiptId,
                        principalSchema: "public",
                        principalTable: "Receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Roll_Stock_StockId",
                        column: x => x.StockId,
                        principalSchema: "public",
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobMaterialUsage",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RollId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityUsedLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobMaterialUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobMaterialUsage_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobMaterialUsage_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobMaterialUsage_Job_JobId",
                        column: x => x.JobId,
                        principalSchema: "public",
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobMaterialUsage_Roll_RollId",
                        column: x => x.RollId,
                        principalSchema: "public",
                        principalTable: "Roll",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_JobMaterialUsage_Stock_StockId",
                        column: x => x.StockId,
                        principalSchema: "public",
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobOperation",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    EquipmentType = table.Column<int>(type: "integer", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlannedMinutes = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    ActualStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    GoodCount = table.Column<int>(type: "integer", nullable: false),
                    WasteCount = table.Column<int>(type: "integer", nullable: false),
                    DowntimeMinutes = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    DowntimeReasonCode = table.Column<int>(type: "integer", nullable: true),
                    ScannedRollId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobOperation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobOperation_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobOperation_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobOperation_AspNetUsers_OperatorId",
                        column: x => x.OperatorId,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobOperation_Job_JobId",
                        column: x => x.JobId,
                        principalSchema: "public",
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobOperation_Roll_ScannedRollId",
                        column: x => x.ScannedRollId,
                        principalSchema: "public",
                        principalTable: "Roll",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RollMovement",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RollId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<int>(type: "integer", nullable: false),
                    QuantityLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollMovement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RollMovement_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RollMovement_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RollMovement_Job_JobId",
                        column: x => x.JobId,
                        principalSchema: "public",
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RollMovement_Roll_RollId",
                        column: x => x.RollId,
                        principalSchema: "public",
                        principalTable: "Roll",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobTimeEntry",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClockedInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClockedOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTimeEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobTimeEntry_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobTimeEntry_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobTimeEntry_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobTimeEntry_JobOperation_JobOperationId",
                        column: x => x.JobOperationId,
                        principalSchema: "public",
                        principalTable: "JobOperation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_CreatedById",
                schema: "public",
                table: "Invoice",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_CustomerId",
                schema: "public",
                table: "Invoice",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_InvoiceNumber",
                schema: "public",
                table: "Invoice",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_ModifiedById",
                schema: "public",
                table: "Invoice",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_SalesOrderId",
                schema: "public",
                table: "Invoice",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_ShipmentId",
                schema: "public",
                table: "Invoice",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_Status",
                schema: "public",
                table: "Invoice",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_CreatedById",
                schema: "public",
                table: "InvoiceLine",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_InvoiceId_LineNumber",
                schema: "public",
                table: "InvoiceLine",
                columns: new[] { "InvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_JobId",
                schema: "public",
                table: "InvoiceLine",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_ModifiedById",
                schema: "public",
                table: "InvoiceLine",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_SalesOrderLineId",
                schema: "public",
                table: "InvoiceLine",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_CreatedById",
                schema: "public",
                table: "Job",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Job_JobNumber",
                schema: "public",
                table: "Job",
                column: "JobNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Job_ModifiedById",
                schema: "public",
                table: "Job",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Job_ProductId",
                schema: "public",
                table: "Job",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_SalesOrderLineId",
                schema: "public",
                table: "Job",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_ScheduledPressId",
                schema: "public",
                table: "Job",
                column: "ScheduledPressId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_Status",
                schema: "public",
                table: "Job",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_JobMaterialUsage_CreatedById",
                schema: "public",
                table: "JobMaterialUsage",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_JobMaterialUsage_JobId",
                schema: "public",
                table: "JobMaterialUsage",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobMaterialUsage_ModifiedById",
                schema: "public",
                table: "JobMaterialUsage",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_JobMaterialUsage_RollId",
                schema: "public",
                table: "JobMaterialUsage",
                column: "RollId");

            migrationBuilder.CreateIndex(
                name: "IX_JobMaterialUsage_StockId",
                schema: "public",
                table: "JobMaterialUsage",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_JobOperation_CreatedById",
                schema: "public",
                table: "JobOperation",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_JobOperation_JobId_Sequence",
                schema: "public",
                table: "JobOperation",
                columns: new[] { "JobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobOperation_ModifiedById",
                schema: "public",
                table: "JobOperation",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_JobOperation_OperatorId",
                schema: "public",
                table: "JobOperation",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_JobOperation_ScannedRollId",
                schema: "public",
                table: "JobOperation",
                column: "ScannedRollId");

            migrationBuilder.CreateIndex(
                name: "IX_JobTimeEntry_CreatedById",
                schema: "public",
                table: "JobTimeEntry",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_JobTimeEntry_JobOperationId",
                schema: "public",
                table: "JobTimeEntry",
                column: "JobOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_JobTimeEntry_ModifiedById",
                schema: "public",
                table: "JobTimeEntry",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_JobTimeEntry_UserId",
                schema: "public",
                table: "JobTimeEntry",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_CreatedById",
                schema: "public",
                table: "Payment",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_InvoiceId",
                schema: "public",
                table: "Payment",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_ModifiedById",
                schema: "public",
                table: "Payment",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_PaymentDate",
                schema: "public",
                table: "Payment",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_CreatedById",
                schema: "public",
                table: "PurchaseOrder",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_ModifiedById",
                schema: "public",
                table: "PurchaseOrder",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_PoNumber",
                schema: "public",
                table: "PurchaseOrder",
                column: "PoNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_Status",
                schema: "public",
                table: "PurchaseOrder",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_SupplierId",
                schema: "public",
                table: "PurchaseOrder",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_CreatedById",
                schema: "public",
                table: "PurchaseOrderLine",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_ModifiedById",
                schema: "public",
                table: "PurchaseOrderLine",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_PurchaseOrderId_LineNumber",
                schema: "public",
                table: "PurchaseOrderLine",
                columns: new[] { "PurchaseOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_StockId",
                schema: "public",
                table: "PurchaseOrderLine",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipt_CreatedById",
                schema: "public",
                table: "Receipt",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Receipt_ModifiedById",
                schema: "public",
                table: "Receipt",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Receipt_PoLineId",
                schema: "public",
                table: "Receipt",
                column: "PoLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Roll_CreatedById",
                schema: "public",
                table: "Roll",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Roll_ModifiedById",
                schema: "public",
                table: "Roll",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Roll_ReceiptId",
                schema: "public",
                table: "Roll",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_Roll_RollBarcode",
                schema: "public",
                table: "Roll",
                column: "RollBarcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roll_Status",
                schema: "public",
                table: "Roll",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Roll_StockId",
                schema: "public",
                table: "Roll",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_RollMovement_CreatedById",
                schema: "public",
                table: "RollMovement",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_RollMovement_JobId",
                schema: "public",
                table: "RollMovement",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_RollMovement_ModifiedById",
                schema: "public",
                table: "RollMovement",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_RollMovement_RollId",
                schema: "public",
                table: "RollMovement",
                column: "RollId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_CreatedById",
                schema: "public",
                table: "Shipment",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_ModifiedById",
                schema: "public",
                table: "Shipment",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_SalesOrderId",
                schema: "public",
                table: "Shipment",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_ShipFromAddressId",
                schema: "public",
                table: "Shipment",
                column: "ShipFromAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_ShipmentNumber",
                schema: "public",
                table: "Shipment",
                column: "ShipmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_ShipToAddressId",
                schema: "public",
                table: "Shipment",
                column: "ShipToAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_Status",
                schema: "public",
                table: "Shipment",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLine_CreatedById",
                schema: "public",
                table: "ShipmentLine",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLine_JobId",
                schema: "public",
                table: "ShipmentLine",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLine_ModifiedById",
                schema: "public",
                table: "ShipmentLine",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLine_SalesOrderLineId",
                schema: "public",
                table: "ShipmentLine",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLine_ShipmentId",
                schema: "public",
                table: "ShipmentLine",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackage_CreatedById",
                schema: "public",
                table: "ShipmentPackage",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackage_ModifiedById",
                schema: "public",
                table: "ShipmentPackage",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackage_ShipmentId_PackageNumber",
                schema: "public",
                table: "ShipmentPackage",
                columns: new[] { "ShipmentId", "PackageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackage_TrackingNumber",
                schema: "public",
                table: "ShipmentPackage",
                column: "TrackingNumber",
                unique: true,
                filter: "\"TrackingNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvent_CreatedById",
                schema: "public",
                table: "TrackingEvent",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvent_EventAt",
                schema: "public",
                table: "TrackingEvent",
                column: "EventAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvent_ModifiedById",
                schema: "public",
                table: "TrackingEvent",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvent_ShipmentPackageId",
                schema: "public",
                table: "TrackingEvent",
                column: "ShipmentPackageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLine",
                schema: "public");

            migrationBuilder.DropTable(
                name: "JobMaterialUsage",
                schema: "public");

            migrationBuilder.DropTable(
                name: "JobTimeEntry",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Payment",
                schema: "public");

            migrationBuilder.DropTable(
                name: "RollMovement",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ShipmentLine",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TrackingEvent",
                schema: "public");

            migrationBuilder.DropTable(
                name: "JobOperation",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Invoice",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ShipmentPackage",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Job",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Roll",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Shipment",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Receipt",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLine",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PurchaseOrder",
                schema: "public");
        }
    }
}
