using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShrinkStockType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ShrinkLayflatIn",
                schema: "public",
                table: "Stock",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Spec_ShrinkLayflatIn",
                schema: "public",
                table: "SalesOrderLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Spec_ShrinkLayflatIn",
                schema: "public",
                table: "Job",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShrinkLayflatIn",
                schema: "public",
                table: "EstimateLine",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShrinkLayflatIn",
                schema: "public",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "Spec_ShrinkLayflatIn",
                schema: "public",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "Spec_ShrinkLayflatIn",
                schema: "public",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "ShrinkLayflatIn",
                schema: "public",
                table: "EstimateLine");
        }
    }
}
