using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentManualShipFromAndNullableService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ShipFromAddressId",
                schema: "public",
                table: "Shipment",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "ServiceLevel",
                schema: "public",
                table: "Shipment",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ShipFromCity",
                schema: "public",
                table: "Shipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipFromCountry",
                schema: "public",
                table: "Shipment",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipFromName",
                schema: "public",
                table: "Shipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipFromState",
                schema: "public",
                table: "Shipment",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipFromStreet1",
                schema: "public",
                table: "Shipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipFromStreet2",
                schema: "public",
                table: "Shipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipFromZip",
                schema: "public",
                table: "Shipment",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipFromCity",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipFromCountry",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipFromName",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipFromState",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipFromStreet1",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipFromStreet2",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipFromZip",
                schema: "public",
                table: "Shipment");

            migrationBuilder.AlterColumn<Guid>(
                name: "ShipFromAddressId",
                schema: "public",
                table: "Shipment",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ServiceLevel",
                schema: "public",
                table: "Shipment",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
