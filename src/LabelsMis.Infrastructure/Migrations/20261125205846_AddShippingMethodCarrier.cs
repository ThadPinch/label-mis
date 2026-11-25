using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingMethodCarrier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Carrier",
                schema: "public",
                table: "ShippingMethod",
                type: "integer",
                nullable: true);

            // Existing carrier methods take the carrier their name says (the seeded one is "FedEx"),
            // so Record shipment keeps starting where it does today: FedEx selected, tracking typed.
            // A name that names no carrier stays "not said". Values are the Carrier enum:
            // 0 FedEx, 1 UPS, 2 USPS, 3 DHL. From here on the setting is edited on the method.
            migrationBuilder.Sql("""
                UPDATE public."ShippingMethod"
                SET "Carrier" = CASE
                    WHEN "Name" ~* '\mfed\s*ex' OR "Name" ~* 'federal\s+express' THEN 0
                    WHEN "Name" ~* '\musps\M' THEN 2
                    WHEN "Name" ~* '\mups\M' THEN 1
                    WHEN "Name" ~* '\mdhl\M' THEN 3
                    ELSE NULL
                END
                WHERE "MethodType" = 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Carrier",
                schema: "public",
                table: "ShippingMethod");
        }
    }
}
