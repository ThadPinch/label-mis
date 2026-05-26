using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateLineMarkupOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MarkupPctOverride",
                schema: "public",
                table: "EstimateLine",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarkupPctOverride",
                schema: "public",
                table: "EstimateLine");
        }
    }
}
