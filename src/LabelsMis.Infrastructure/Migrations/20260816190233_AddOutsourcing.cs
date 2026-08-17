using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutsourcing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOutsourceVendor",
                schema: "public",
                table: "Supplier",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OutsourceNotes",
                schema: "public",
                table: "Supplier",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOutsourced",
                schema: "public",
                table: "Job",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedTotalPrice",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedUnitPrice",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OutsourceCost",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            // Existing breaks were never outsourced, so the price the calculator produced is the
            // price that was quoted — backfill so history compares correctly.
            migrationBuilder.Sql(
                """
                UPDATE public."EstimateQuantityBreak"
                SET "CalculatedUnitPrice" = "UnitPrice",
                    "CalculatedTotalPrice" = "TotalPrice";
                """);

            migrationBuilder.AddColumn<bool>(
                name: "IsOutsourced",
                schema: "public",
                table: "EstimateLine",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OutsourceExpectedIn",
                schema: "public",
                table: "EstimateLine",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutsourcePrivateNotes",
                schema: "public",
                table: "EstimateLine",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutsourceQuoteNumber",
                schema: "public",
                table: "EstimateLine",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OutsourceVendorId",
                schema: "public",
                table: "EstimateLine",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOutsourced",
                schema: "public",
                table: "EstimateCharge",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OutsourceCost",
                schema: "public",
                table: "EstimateCharge",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OutsourceExpectedIn",
                schema: "public",
                table: "EstimateCharge",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutsourcePrivateNotes",
                schema: "public",
                table: "EstimateCharge",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutsourceQuoteNumber",
                schema: "public",
                table: "EstimateCharge",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OutsourceVendorId",
                schema: "public",
                table: "EstimateCharge",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutsourcedItem",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalesOrderChargeId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuoteNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VendorCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ExpectedIn = table.Column<DateOnly>(type: "date", nullable: true),
                    PrivateNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SentToVendorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutsourcedItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutsourcedItem_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsourcedItem_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsourcedItem_SalesOrderCharge_SalesOrderChargeId",
                        column: x => x.SalesOrderChargeId,
                        principalSchema: "public",
                        principalTable: "SalesOrderCharge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutsourcedItem_SalesOrderLine_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalSchema: "public",
                        principalTable: "SalesOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutsourcedItem_SalesOrder_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalSchema: "public",
                        principalTable: "SalesOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutsourcedItem_Supplier_VendorId",
                        column: x => x.VendorId,
                        principalSchema: "public",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutsourceReceipt",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutsourcedItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutsourceReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutsourceReceipt_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsourceReceipt_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsourceReceipt_OutsourcedItem_OutsourcedItemId",
                        column: x => x.OutsourcedItemId,
                        principalSchema: "public",
                        principalTable: "OutsourcedItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EstimateLine_OutsourceVendorId",
                schema: "public",
                table: "EstimateLine",
                column: "OutsourceVendorId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateCharge_OutsourceVendorId",
                schema: "public",
                table: "EstimateCharge",
                column: "OutsourceVendorId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItem_CreatedById",
                schema: "public",
                table: "OutsourcedItem",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItem_ModifiedById",
                schema: "public",
                table: "OutsourcedItem",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItem_SalesOrderChargeId",
                schema: "public",
                table: "OutsourcedItem",
                column: "SalesOrderChargeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItem_SalesOrderId",
                schema: "public",
                table: "OutsourcedItem",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItem_SalesOrderLineId",
                schema: "public",
                table: "OutsourcedItem",
                column: "SalesOrderLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItem_VendorId",
                schema: "public",
                table: "OutsourcedItem",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourceReceipt_CreatedById",
                schema: "public",
                table: "OutsourceReceipt",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourceReceipt_ModifiedById",
                schema: "public",
                table: "OutsourceReceipt",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourceReceipt_OutsourcedItemId",
                schema: "public",
                table: "OutsourceReceipt",
                column: "OutsourcedItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_EstimateCharge_Supplier_OutsourceVendorId",
                schema: "public",
                table: "EstimateCharge",
                column: "OutsourceVendorId",
                principalSchema: "public",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EstimateLine_Supplier_OutsourceVendorId",
                schema: "public",
                table: "EstimateLine",
                column: "OutsourceVendorId",
                principalSchema: "public",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EstimateCharge_Supplier_OutsourceVendorId",
                schema: "public",
                table: "EstimateCharge");

            migrationBuilder.DropForeignKey(
                name: "FK_EstimateLine_Supplier_OutsourceVendorId",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropTable(
                name: "OutsourceReceipt",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OutsourcedItem",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_EstimateLine_OutsourceVendorId",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropIndex(
                name: "IX_EstimateCharge_OutsourceVendorId",
                schema: "public",
                table: "EstimateCharge");

            migrationBuilder.DropColumn(
                name: "IsOutsourceVendor",
                schema: "public",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "OutsourceNotes",
                schema: "public",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "IsOutsourced",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "CalculatedTotalPrice",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropColumn(
                name: "CalculatedUnitPrice",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropColumn(
                name: "OutsourceCost",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropColumn(
                name: "IsOutsourced",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "OutsourceExpectedIn",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "OutsourcePrivateNotes",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "OutsourceQuoteNumber",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "OutsourceVendorId",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "IsOutsourced",
                schema: "public",
                table: "EstimateCharge");

            migrationBuilder.DropColumn(
                name: "OutsourceCost",
                schema: "public",
                table: "EstimateCharge");

            migrationBuilder.DropColumn(
                name: "OutsourceExpectedIn",
                schema: "public",
                table: "EstimateCharge");

            migrationBuilder.DropColumn(
                name: "OutsourcePrivateNotes",
                schema: "public",
                table: "EstimateCharge");

            migrationBuilder.DropColumn(
                name: "OutsourceQuoteNumber",
                schema: "public",
                table: "EstimateCharge");

            migrationBuilder.DropColumn(
                name: "OutsourceVendorId",
                schema: "public",
                table: "EstimateCharge");
        }
    }
}
