using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                schema: "public",
                table: "TrackingEvent",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierBoxKey",
                schema: "public",
                table: "ShipmentPackage",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierPackageId",
                schema: "public",
                table: "ShipmentPackage",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrackingStatus",
                schema: "public",
                table: "ShipmentPackage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierBillToAccount",
                schema: "public",
                table: "Shipment",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierBillToPostalCode",
                schema: "public",
                table: "Shipment",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CarrierEnvironment",
                schema: "public",
                table: "Shipment",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierOfferId",
                schema: "public",
                table: "Shipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierOrderNumber",
                schema: "public",
                table: "Shipment",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierQuoteId",
                schema: "public",
                table: "Shipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierServiceCode",
                schema: "public",
                table: "Shipment",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierServiceName",
                schema: "public",
                table: "Shipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LabelPurchasedAt",
                schema: "public",
                table: "Shipment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LabelState",
                schema: "public",
                table: "Shipment",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ShipToContact",
                schema: "public",
                table: "Shipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToPhone",
                schema: "public",
                table: "Shipment",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShipToResidential",
                schema: "public",
                table: "Shipment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrackingCheckedAt",
                schema: "public",
                table: "Shipment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                schema: "public",
                table: "Shipment",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                schema: "public",
                table: "Shipment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedById",
                schema: "public",
                table: "Shipment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultLabelFormat",
                schema: "public",
                table: "CarrierSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TrackingEvent_ShipmentPackageId_CarrierCode_EventAt",
                schema: "public",
                table: "TrackingEvent",
                columns: new[] { "ShipmentPackageId", "CarrierCode", "EventAt" },
                unique: true,
                filter: "\"CarrierCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_CarrierQuoteId",
                schema: "public",
                table: "Shipment",
                column: "CarrierQuoteId",
                unique: true,
                filter: "\"CarrierQuoteId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackingEvent_ShipmentPackageId_CarrierCode_EventAt",
                schema: "public",
                table: "TrackingEvent");

            migrationBuilder.DropIndex(
                name: "IX_Shipment_CarrierQuoteId",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                schema: "public",
                table: "TrackingEvent");

            migrationBuilder.DropColumn(
                name: "CarrierBoxKey",
                schema: "public",
                table: "ShipmentPackage");

            migrationBuilder.DropColumn(
                name: "CarrierPackageId",
                schema: "public",
                table: "ShipmentPackage");

            migrationBuilder.DropColumn(
                name: "TrackingStatus",
                schema: "public",
                table: "ShipmentPackage");

            migrationBuilder.DropColumn(
                name: "CarrierBillToAccount",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierBillToPostalCode",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierEnvironment",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierOfferId",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierOrderNumber",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierQuoteId",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierServiceCode",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "CarrierServiceName",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "LabelPurchasedAt",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "LabelState",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToContact",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToPhone",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "ShipToResidential",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "TrackingCheckedAt",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "VoidedById",
                schema: "public",
                table: "Shipment");

            migrationBuilder.DropColumn(
                name: "DefaultLabelFormat",
                schema: "public",
                table: "CarrierSettings");
        }
    }
}
