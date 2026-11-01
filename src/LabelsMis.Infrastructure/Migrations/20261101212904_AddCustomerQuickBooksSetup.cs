using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerQuickBooksSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuickBooksCustomerId",
                schema: "public",
                table: "Customer",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TaxSetupComplete",
                schema: "public",
                table: "Customer",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TaxSetupCompletedAt",
                schema: "public",
                table: "Customer",
                type: "date",
                nullable: true);

            // Customers already billed through QuickBooks (any invoice marked exported) went
            // through the new-client form long ago: mark them set up, dated by their first export,
            // so deploying this doesn't move every existing invoice to Waiting. Customers never
            // exported stay gated until someone ticks the box. Only touches rows still false, so a
            // re-run changes nothing.
            migrationBuilder.Sql("""
                UPDATE public."Customer" AS c
                SET "TaxSetupComplete" = true,
                    "TaxSetupCompletedAt" = first_export.exported_on
                FROM (
                    SELECT "CustomerId", min("QbExportedAt")::date AS exported_on
                    FROM public."Invoice"
                    WHERE "QbExportedAt" IS NOT NULL
                    GROUP BY "CustomerId"
                ) AS first_export
                WHERE c."Id" = first_export."CustomerId"
                  AND c."TaxSetupComplete" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuickBooksCustomerId",
                schema: "public",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "TaxSetupComplete",
                schema: "public",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "TaxSetupCompletedAt",
                schema: "public",
                table: "Customer");
        }
    }
}
