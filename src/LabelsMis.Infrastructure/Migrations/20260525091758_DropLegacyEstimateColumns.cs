using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyEstimateColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Estimate_Stock_SubstrateId",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropForeignKey(
                name: "FK_EstimateQuantityBreak_Estimate_EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropForeignKey(
                name: "FK_Product_Estimate_SourceEstimateId",
                schema: "public",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Product_SourceEstimateId",
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

            migrationBuilder.DropIndex(
                name: "IX_Estimate_SubstrateId",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "SourceEstimateId",
                schema: "public",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropColumn(
                name: "BleedIn",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "CornerRadiusIn",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "FinishingOperationsJson",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "GutterAcrossIn",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "GutterAroundIn",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "InkSet",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "LabelAcrossIn",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "LabelAroundIn",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ProductDescription",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "RunningWastePct",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "SetupWasteImpressions",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "SubstrateId",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "WhiteInkUsed",
                schema: "public",
                table: "Estimate");

            migrationBuilder.AlterColumn<Guid>(
                name: "EstimateLineId",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstimateQuantityBreak_EstimateLineId_Quantity",
                schema: "public",
                table: "EstimateQuantityBreak",
                columns: new[] { "EstimateLineId", "Quantity" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EstimateQuantityBreak_EstimateLineId_Quantity",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEstimateId",
                schema: "public",
                table: "Product",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "EstimateLineId",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BleedIn",
                schema: "public",
                table: "Estimate",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CornerRadiusIn",
                schema: "public",
                table: "Estimate",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FinishingOperationsJson",
                schema: "public",
                table: "Estimate",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "GutterAcrossIn",
                schema: "public",
                table: "Estimate",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GutterAroundIn",
                schema: "public",
                table: "Estimate",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "InkSet",
                schema: "public",
                table: "Estimate",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LabelAcrossIn",
                schema: "public",
                table: "Estimate",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LabelAroundIn",
                schema: "public",
                table: "Estimate",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ProductDescription",
                schema: "public",
                table: "Estimate",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RunningWastePct",
                schema: "public",
                table: "Estimate",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SetupWasteImpressions",
                schema: "public",
                table: "Estimate",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SubstrateId",
                schema: "public",
                table: "Estimate",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhiteInkUsed",
                schema: "public",
                table: "Estimate",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Product_SourceEstimateId",
                schema: "public",
                table: "Product",
                column: "SourceEstimateId");

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
                name: "IX_Estimate_SubstrateId",
                schema: "public",
                table: "Estimate",
                column: "SubstrateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Estimate_Stock_SubstrateId",
                schema: "public",
                table: "Estimate",
                column: "SubstrateId",
                principalSchema: "public",
                principalTable: "Stock",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EstimateQuantityBreak_Estimate_EstimateId",
                schema: "public",
                table: "EstimateQuantityBreak",
                column: "EstimateId",
                principalSchema: "public",
                principalTable: "Estimate",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_Estimate_SourceEstimateId",
                schema: "public",
                table: "Product",
                column: "SourceEstimateId",
                principalSchema: "public",
                principalTable: "Estimate",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
