using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                schema: "public",
                table: "Estimate",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountry",
                schema: "public",
                table: "Estimate",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToName",
                schema: "public",
                table: "Estimate",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToState",
                schema: "public",
                table: "Estimate",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet1",
                schema: "public",
                table: "Estimate",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet2",
                schema: "public",
                table: "Estimate",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToZip",
                schema: "public",
                table: "Estimate",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingCost",
                schema: "public",
                table: "Estimate",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "ShippingMethodId",
                schema: "public",
                table: "Estimate",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Estimate_ShippingMethodId",
                schema: "public",
                table: "Estimate",
                column: "ShippingMethodId");

            migrationBuilder.AddForeignKey(
                name: "FK_Estimate_ShippingMethod_ShippingMethodId",
                schema: "public",
                table: "Estimate",
                column: "ShippingMethodId",
                principalSchema: "public",
                principalTable: "ShippingMethod",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Estimate_ShippingMethod_ShippingMethodId",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropIndex(
                name: "IX_Estimate_ShippingMethodId",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShipToCity",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShipToCountry",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShipToName",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShipToState",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShipToStreet1",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShipToStreet2",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShipToZip",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShippingCost",
                schema: "public",
                table: "Estimate");

            migrationBuilder.DropColumn(
                name: "ShippingMethodId",
                schema: "public",
                table: "Estimate");
        }
    }
}
