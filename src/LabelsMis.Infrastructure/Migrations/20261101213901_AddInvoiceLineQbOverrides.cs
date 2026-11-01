using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineQbOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QbClassOverride",
                schema: "public",
                table: "InvoiceLine",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QbItemOverride",
                schema: "public",
                table: "InvoiceLine",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QbClassOverride",
                schema: "public",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "QbItemOverride",
                schema: "public",
                table: "InvoiceLine");
        }
    }
}
