using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderRepAndLineDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "public",
                table: "SalesOrderLine",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesRepId",
                schema: "public",
                table: "SalesOrder",
                type: "uuid",
                nullable: true);

            // Backfill orders converted before these columns existed from their source estimate.
            migrationBuilder.Sql("""
                UPDATE public."SalesOrder" o
                SET "SalesRepId" = e."SalesRepId"
                FROM public."Estimate" e
                WHERE o."SourceEstimateId" = e."Id" AND e."SalesRepId" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE public."SalesOrderLine" l
                SET "Description" = el."ProductDescription"
                FROM public."EstimateLine" el
                WHERE l."SourceEstimateLineId" = el."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "SalesRepId",
                schema: "public",
                table: "SalesOrder");
        }
    }
}
