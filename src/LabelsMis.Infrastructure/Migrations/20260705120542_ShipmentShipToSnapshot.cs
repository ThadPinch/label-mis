using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentShipToSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ShipToAddressId",
                schema: "public",
                table: "Shipment",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                schema: "public",
                table: "Shipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountry",
                schema: "public",
                table: "Shipment",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToName",
                schema: "public",
                table: "Shipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToState",
                schema: "public",
                table: "Shipment",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet1",
                schema: "public",
                table: "Shipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet2",
                schema: "public",
                table: "Shipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToZip",
                schema: "public",
                table: "Shipment",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Backfill the new snapshot columns from the linked address for existing shipments.
            migrationBuilder.Sql("""
                UPDATE "public"."Shipment" s
                SET "ShipToStreet1" = a."Street1",
                    "ShipToStreet2" = a."Street2",
                    "ShipToCity"    = a."City",
                    "ShipToState"   = a."State",
                    "ShipToZip"     = a."Zip",
                    "ShipToCountry" = a."Country"
                FROM "public"."Address" a
                WHERE s."ShipToAddressId" = a."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipToCity",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToCountry",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToName",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToState",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToStreet1",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToStreet2",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToZip",
                schema: "public",
                table: "Shipment");

            migrationBuilder.AlterColumn<Guid>(
                name: "ShipToAddressId",
                schema: "public",
                table: "Shipment",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
