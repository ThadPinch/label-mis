using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingMethodRequiresShipmentRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresShipmentRecord",
                schema: "public",
                table: "ShippingMethod",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Carrier methods (type 2 = Fedex, the only carrier type) bill shipping from the
            // shipment's carrier cost, so they keep needing the recorded
            // shipment. Pickup, local delivery and customer account stay false: production's Mark
            // shipped records their package-less shipment by itself. Editable per method afterwards.
            migrationBuilder.Sql("""
                UPDATE public."ShippingMethod"
                SET "RequiresShipmentRecord" = true
                WHERE "MethodType" = 2
                  AND "RequiresShipmentRecord" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresShipmentRecord",
                schema: "public",
                table: "ShippingMethod");
        }
    }
}
