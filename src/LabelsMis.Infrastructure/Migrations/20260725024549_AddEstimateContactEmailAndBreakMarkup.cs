using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateContactEmailAndBreakMarkup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MarkupPctOverride",
                schema: "public",
                table: "EstimateQuantityBreak",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                schema: "public",
                table: "Estimate",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarkupPctOverride",
                schema: "public",
                table: "EstimateQuantityBreak");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                schema: "public",
                table: "Estimate");
        }
    }
}
