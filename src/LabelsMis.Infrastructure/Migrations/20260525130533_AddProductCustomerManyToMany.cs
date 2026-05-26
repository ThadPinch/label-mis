using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCustomerManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Product_Customer_CustomerId",
                schema: "public",
                table: "Product");

            migrationBuilder.RenameColumn(
                name: "CustomerId",
                schema: "public",
                table: "Product",
                newName: "PrimaryCustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Product_CustomerId_InternalSku",
                schema: "public",
                table: "Product",
                newName: "IX_Product_PrimaryCustomerId_InternalSku");

            migrationBuilder.CreateTable(
                name: "ProductCustomer",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCustomer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductCustomer_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCustomer_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCustomer_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCustomer_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "public",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCustomer_CreatedById",
                schema: "public",
                table: "ProductCustomer",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCustomer_CustomerId",
                schema: "public",
                table: "ProductCustomer",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCustomer_ModifiedById",
                schema: "public",
                table: "ProductCustomer",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCustomer_ProductId_CustomerId",
                schema: "public",
                table: "ProductCustomer",
                columns: new[] { "ProductId", "CustomerId" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "ProductCustomer" ("Id", "ProductId", "CustomerId", "TenantId", "CreatedAt", "CreatedById")
                SELECT gen_random_uuid(), "Id", "PrimaryCustomerId", "TenantId", "CreatedAt", "CreatedById"
                FROM "Product";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_Customer_PrimaryCustomerId",
                schema: "public",
                table: "Product",
                column: "PrimaryCustomerId",
                principalSchema: "public",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Product_Customer_PrimaryCustomerId",
                schema: "public",
                table: "Product");

            migrationBuilder.DropTable(
                name: "ProductCustomer",
                schema: "public");

            migrationBuilder.RenameColumn(
                name: "PrimaryCustomerId",
                schema: "public",
                table: "Product",
                newName: "CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Product_PrimaryCustomerId_InternalSku",
                schema: "public",
                table: "Product",
                newName: "IX_Product_CustomerId_InternalSku");

            migrationBuilder.AddForeignKey(
                name: "FK_Product_Customer_CustomerId",
                schema: "public",
                table: "Product",
                column: "CustomerId",
                principalSchema: "public",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
