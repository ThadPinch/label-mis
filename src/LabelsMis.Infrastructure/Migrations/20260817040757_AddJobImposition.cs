using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobImposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImposedArtworkFilePath",
                schema: "public",
                table: "Job",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImposedAt",
                schema: "public",
                table: "Job",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImposedFromArtworkFilePath",
                schema: "public",
                table: "Job",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionBleedIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionCornerRadiusIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionCrossWebOffsetIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionEyeMarkHeightIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionEyeMarkWidthIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImpositionEyeMarks",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionGutterAcrossIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionGutterAroundIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImpositionIncludeDieLines",
                schema: "public",
                table: "Job",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImpositionIncludeSlug",
                schema: "public",
                table: "Job",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionLabelAcrossIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionLabelAroundIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImpositionLabelsAcross",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImpositionLabelsAround",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImpositionOrientation",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImpositionWebWidthIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImposedArtworkFilePath",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImposedAt",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImposedFromArtworkFilePath",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionBleedIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionCornerRadiusIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionCrossWebOffsetIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionEyeMarkHeightIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionEyeMarkWidthIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionEyeMarks",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionGutterAcrossIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionGutterAroundIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionIncludeDieLines",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionIncludeSlug",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionLabelAcrossIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionLabelAroundIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionLabelsAcross",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionLabelsAround",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionOrientation",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ImpositionWebWidthIn",
                schema: "public",
                table: "Job");
        }
    }
}
