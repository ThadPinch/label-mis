using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateLineEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EstimateQuantityBreak_EstimateId_Quantity",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEstimateLineId",
                schema: "public",
                table: "SalesOrderLine",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEstimateId",
                schema: "public",
                table: "SalesOrder",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEstimateLineId",
                schema: "public",
                table: "Product",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "EstimateLineId",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SubstrateId",
                schema: "public",
                table: "Estimate",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "EstimateLine",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceProductId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    LineNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstimateLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstimateLine_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstimateLine_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstimateLine_Estimate_EstimateId",
                        column: x => x.EstimateId,
                        principalSchema: "public",
                        principalTable: "Estimate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EstimateLine_Product_SourceProductId",
                        column: x => x.SourceProductId,
                        principalSchema: "public",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EstimateLine_Stock_SubstrateId",
                        column: x => x.SubstrateId,
                        principalSchema: "public",
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_SourceEstimateLineId",
                schema: "public",
                table: "SalesOrderLine",
                column: "SourceEstimateLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_SourceEstimateId",
                schema: "public",
                table: "SalesOrder",
                column: "SourceEstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_SourceEstimateLineId",
                schema: "public",
                table: "Product",
                column: "SourceEstimateLineId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateQuantityBreak_EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateQuantityBreak_EstimateLineId_Quantity",
                schema: "public",
                table: "EstimateQuantityBreak",
                columns: new[] { "EstimateLineId", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_EstimateLine_CreatedById",
                schema: "public",
                table: "EstimateLine",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateLine_EstimateId_LineNumber",
                schema: "public",
                table: "EstimateLine",
                columns: new[] { "EstimateId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstimateLine_ModifiedById",
                schema: "public",
                table: "EstimateLine",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateLine_SourceProductId",
                schema: "public",
                table: "EstimateLine",
                column: "SourceProductId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateLine_SubstrateId",
                schema: "public",
                table: "EstimateLine",
                column: "SubstrateId");

            migrationBuilder.AddForeignKey(
                name: "FK_EstimateQuantityBreak_EstimateLine_EstimateLineId",
                schema: "public",
                table: "EstimateQuantityBreak",
                column: "EstimateLineId",
                principalSchema: "public",
                principalTable: "EstimateLine",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_EstimateLine_SourceEstimateLineId",
                schema: "public",
                table: "Product",
                column: "SourceEstimateLineId",
                principalSchema: "public",
                principalTable: "EstimateLine",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrder_Estimate_SourceEstimateId",
                schema: "public",
                table: "SalesOrder",
                column: "SourceEstimateId",
                principalSchema: "public",
                principalTable: "Estimate",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLine_EstimateLine_SourceEstimateLineId",
                schema: "public",
                table: "SalesOrderLine",
                column: "SourceEstimateLineId",
                principalSchema: "public",
                principalTable: "EstimateLine",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EstimateQuantityBreak_EstimateLine_EstimateLineId",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropForeignKey(
                name: "FK_Product_EstimateLine_SourceEstimateLineId",
                schema: "public",
                table: "Product");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrder_Estimate_SourceEstimateId",
                schema: "public",
                table: "SalesOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLine_EstimateLine_SourceEstimateLineId",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropTable(
                name: "EstimateLine",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLine_SourceEstimateLineId",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrder_SourceEstimateId",
                schema: "public",
                table: "SalesOrder");

            migrationBuilder.DropIndex(
                name: "IX_Product_SourceEstimateLineId",
                schema: "public",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_EstimateQuantityBreak_EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropIndex(
                name: "IX_EstimateQuantityBreak_EstimateLineId_Quantity",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropColumn(
                name: "SourceEstimateLineId",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SourceEstimateId",
                schema: "public",
                table: "SalesOrder");

            migrationBuilder.DropColumn(
                name: "SourceEstimateLineId",
                schema: "public",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "EstimateLineId",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.AlterColumn<Guid>(
                name: "EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SubstrateId",
                schema: "public",
                table: "Estimate",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstimateQuantityBreak_EstimateId_Quantity",
                schema: "public",
                table: "EstimateQuantityBreak",
                columns: new[] { "EstimateId", "Quantity" },
                unique: true);
        }
    }
}
