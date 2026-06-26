using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInkSpeedOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SpeedFpm1Hit",
                schema: "public",
                table: "Ink",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpeedFpm2Hit",
                schema: "public",
                table: "Ink",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpeedFpm3Hit",
                schema: "public",
                table: "Ink",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeedFpm1Hit",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "SpeedFpm2Hit",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "SpeedFpm3Hit",
                schema: "public",
                table: "Ink");
        }
    }
}
