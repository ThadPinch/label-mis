using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpecArtworkFilePath",
                schema: "public",
                table: "Job",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecBleedIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecCornerRadiusIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpecDieId",
                schema: "public",
                table: "Job",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecFinishingOperationsJson",
                schema: "public",
                table: "Job",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecGutterAcrossIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecGutterAroundIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecInkSet",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecLabelAcrossIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecLabelAroundIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecLabelOrientationOverride",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecMaxLabelsAcrossOverride",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecRunningWastePct",
                schema: "public",
                table: "Job",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecSetupWasteImpressions",
                schema: "public",
                table: "Job",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecSpotsJson",
                schema: "public",
                table: "Job",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpecSubstrateId",
                schema: "public",
                table: "Job",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecWhiteCoveragePct",
                schema: "public",
                table: "Job",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecWhiteHits",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecArtworkFilePath",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecBleedIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecCornerRadiusIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecDieId",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecFinishingOperationsJson",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecGutterAcrossIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecGutterAroundIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecInkSet",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecLabelAcrossIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecLabelAroundIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecLabelOrientationOverride",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecMaxLabelsAcrossOverride",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecRunningWastePct",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecSetupWasteImpressions",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecSpotsJson",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecSubstrateId",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecWhiteCoveragePct",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "SpecWhiteHits",
                schema: "public",
                table: "Job");
        }
    }
}
