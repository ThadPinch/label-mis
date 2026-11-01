using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNonBillable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NonBillable",
                schema: "public",
                table: "Invoice",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NonBillableAt",
                schema: "public",
                table: "Invoice",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NonBillableById",
                schema: "public",
                table: "Invoice",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NonBillableReason",
                schema: "public",
                table: "Invoice",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NonBillable",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "NonBillableAt",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "NonBillableById",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "NonBillableReason",
                schema: "public",
                table: "Invoice");
        }
    }
}
