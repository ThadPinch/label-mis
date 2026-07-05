using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderLineSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpecArtworkFilePath",
                schema: "public",
                table: "SalesOrderLine",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecBleedIn",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecCornerRadiusIn",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpecDieId",
                schema: "public",
                table: "SalesOrderLine",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecFinishingOperationsJson",
                schema: "public",
                table: "SalesOrderLine",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecGutterAcrossIn",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecGutterAroundIn",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecInkSet",
                schema: "public",
                table: "SalesOrderLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecLabelAcrossIn",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecLabelAroundIn",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecLabelOrientationOverride",
                schema: "public",
                table: "SalesOrderLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecMaxLabelsAcrossOverride",
                schema: "public",
                table: "SalesOrderLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecRunningWastePct",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecSetupWasteImpressions",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecSpotsJson",
                schema: "public",
                table: "SalesOrderLine",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpecSubstrateId",
                schema: "public",
                table: "SalesOrderLine",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecWhiteCoveragePct",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecWhiteHits",
                schema: "public",
                table: "SalesOrderLine",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecArtworkFilePath",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecBleedIn",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecCornerRadiusIn",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecDieId",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecFinishingOperationsJson",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecGutterAcrossIn",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecGutterAroundIn",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecInkSet",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecLabelAcrossIn",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecLabelAroundIn",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecLabelOrientationOverride",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecMaxLabelsAcrossOverride",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecRunningWastePct",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecSetupWasteImpressions",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecSpotsJson",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecSubstrateId",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecWhiteCoveragePct",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SpecWhiteHits",
                schema: "public",
                table: "SalesOrderLine");
        }
    }
}
