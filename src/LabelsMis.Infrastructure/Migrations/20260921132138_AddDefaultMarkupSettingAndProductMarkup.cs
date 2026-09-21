using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultMarkupSettingAndProductMarkup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MarkupPctOverride",
                schema: "public",
                table: "Product",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMarkupPct",
                schema: "public",
                table: "GeneralSettings",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0.55m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarkupPctOverride",
                schema: "public",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "DefaultMarkupPct",
                schema: "public",
                table: "GeneralSettings");
        }
    }
}
