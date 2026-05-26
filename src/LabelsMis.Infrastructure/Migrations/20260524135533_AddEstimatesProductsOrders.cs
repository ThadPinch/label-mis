using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimatesProductsOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentSequence",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Estimate",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EstimateNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesRepId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProductDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LabelAcrossIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    LabelAroundIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    CornerRadiusIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    GutterAcrossIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    GutterAroundIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    BleedIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    SubstrateId = table.Column<Guid>(type: "uuid", nullable: false),
                    InkSet = table.Column<int>(type: "integer", nullable: false),
                    WhiteInkUsed = table.Column<bool>(type: "boolean", nullable: false),
                    FinishingOperationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SetupWasteImpressions = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    RunningWastePct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ValidUntilDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WonAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LostAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LostReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PdfFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Estimate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Estimate_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Estimate_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Estimate_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Estimate_Stock_SubstrateId",
                        column: x => x.SubstrateId,
                        principalSchema: "public",
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrder",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerPoNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OrderedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedShipDate = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_SalesOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrder_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EstimateQuantityBreak",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CalculatedCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MarginPct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CostBreakdownJson = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstimateQuantityBreak", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstimateQuantityBreak_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstimateQuantityBreak_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstimateQuantityBreak_Estimate_EstimateId",
                        column: x => x.EstimateId,
                        principalSchema: "public",
                        principalTable: "Estimate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EstimateRevision",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstimateRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstimateRevision_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstimateRevision_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstimateRevision_Estimate_EstimateId",
                        column: x => x.EstimateId,
                        principalSchema: "public",
                        principalTable: "Estimate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Product",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InternalSku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceEstimateId = table.Column<Guid>(type: "uuid", nullable: true),
                    LabelAcrossIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    LabelAroundIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    CornerRadiusIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    SubstrateId = table.Column<Guid>(type: "uuid", nullable: false),
                    InkSet = table.Column<int>(type: "integer", nullable: false),
                    FinishingOperationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DieId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArtworkFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Product_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Product_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Product_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Product_Die_DieId",
                        column: x => x.DieId,
                        principalSchema: "public",
                        principalTable: "Die",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Product_Estimate_SourceEstimateId",
                        column: x => x.SourceEstimateId,
                        principalSchema: "public",
                        principalTable: "Estimate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Product_Stock_SubstrateId",
                        column: x => x.SubstrateId,
                        principalSchema: "public",
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RollSpec",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelsPerRoll = table.Column<int>(type: "integer", nullable: false),
                    CoreSizeIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    UnwindPosition = table.Column<int>(type: "integer", nullable: false),
                    MaxOdIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    RollsPerCase = table.Column<int>(type: "integer", nullable: false),
                    CaseLabelFormat = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollSpec", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RollSpec_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RollSpec_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RollSpec_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "public",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLine",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "public",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_SalesOrder_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalSchema: "public",
                        principalTable: "SalesOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSequence_DocumentType_Year",
                schema: "public",
                table: "DocumentSequence",
                columns: new[] { "DocumentType", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Estimate_CreatedById",
                schema: "public",
                table: "Estimate",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Estimate_CustomerId",
                schema: "public",
                table: "Estimate",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Estimate_EstimateNumber",
                schema: "public",
                table: "Estimate",
                column: "EstimateNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Estimate_ModifiedById",
                schema: "public",
                table: "Estimate",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Estimate_Status",
                schema: "public",
                table: "Estimate",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Estimate_SubstrateId",
                schema: "public",
                table: "Estimate",
                column: "SubstrateId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateQuantityBreak_CreatedById",
                schema: "public",
                table: "EstimateQuantityBreak",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateQuantityBreak_EstimateId_Quantity",
                schema: "public",
                table: "EstimateQuantityBreak",
                columns: new[] { "EstimateId", "Quantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstimateQuantityBreak_ModifiedById",
                schema: "public",
                table: "EstimateQuantityBreak",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateRevision_CreatedById",
                schema: "public",
                table: "EstimateRevision",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateRevision_EstimateId_RevisionNumber",
                schema: "public",
                table: "EstimateRevision",
                columns: new[] { "EstimateId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstimateRevision_ModifiedById",
                schema: "public",
                table: "EstimateRevision",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Product_CreatedById",
                schema: "public",
                table: "Product",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Product_CustomerId_InternalSku",
                schema: "public",
                table: "Product",
                columns: new[] { "CustomerId", "InternalSku" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_DieId",
                schema: "public",
                table: "Product",
                column: "DieId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_InternalSku",
                schema: "public",
                table: "Product",
                column: "InternalSku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_ModifiedById",
                schema: "public",
                table: "Product",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Product_SourceEstimateId",
                schema: "public",
                table: "Product",
                column: "SourceEstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_SubstrateId",
                schema: "public",
                table: "Product",
                column: "SubstrateId");

            migrationBuilder.CreateIndex(
                name: "IX_RollSpec_CreatedById",
                schema: "public",
                table: "RollSpec",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_RollSpec_ModifiedById",
                schema: "public",
                table: "RollSpec",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_RollSpec_ProductId",
                schema: "public",
                table: "RollSpec",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_CreatedById",
                schema: "public",
                table: "SalesOrder",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_CustomerId",
                schema: "public",
                table: "SalesOrder",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_ModifiedById",
                schema: "public",
                table: "SalesOrder",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_OrderNumber",
                schema: "public",
                table: "SalesOrder",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_Status",
                schema: "public",
                table: "SalesOrder",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_CreatedById",
                schema: "public",
                table: "SalesOrderLine",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_ModifiedById",
                schema: "public",
                table: "SalesOrderLine",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_ProductId",
                schema: "public",
                table: "SalesOrderLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_SalesOrderId_LineNumber",
                schema: "public",
                table: "SalesOrderLine",
                columns: new[] { "SalesOrderId", "LineNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSequence",
                schema: "public");

            migrationBuilder.DropTable(
                name: "EstimateQuantityBreak",
                schema: "public");

            migrationBuilder.DropTable(
                name: "EstimateRevision",
                schema: "public");

            migrationBuilder.DropTable(
                name: "RollSpec",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SalesOrderLine",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Product",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SalesOrder",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Estimate",
                schema: "public");
        }
    }
}
