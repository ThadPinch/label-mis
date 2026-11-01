using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentBilledShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BilledShipping",
                schema: "public",
                table: "Shipment",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarrierCostMultiplier",
                schema: "public",
                table: "GeneralSettings",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "LocalDeliveryFee",
                schema: "public",
                table: "GeneralSettings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 25m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BilledShipping",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierCostMultiplier",
                schema: "public",
                table: "GeneralSettings");

            migrationBuilder.DropColumn(
                name: "LocalDeliveryFee",
                schema: "public",
                table: "GeneralSettings");
        }
    }
}
