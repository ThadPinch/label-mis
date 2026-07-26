using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnwindDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Spec_Unwind",
                schema: "public",
                table: "SalesOrderLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Spec_Unwind",
                schema: "public",
                table: "Job",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Unwind",
                schema: "public",
                table: "EstimateLine",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Spec_Unwind",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "Spec_Unwind",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "Unwind",
                schema: "public",
                table: "EstimateLine");
        }
    }
}
