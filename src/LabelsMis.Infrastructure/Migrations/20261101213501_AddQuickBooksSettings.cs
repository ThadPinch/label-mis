using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickBooksSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuickBooksSettings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesItem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShippingItem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DesignItem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RushItem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DieCreationItem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RollLabelClass = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShrinkSleevesClass = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BuyoutClass = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DesignKeywords = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RushKeywords = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DieCreationKeywords = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OutsourcedSubstrateCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickBooksSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuickBooksSettings_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuickBooksSettings_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksSettings_CreatedById",
                schema: "public",
                table: "QuickBooksSettings",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksSettings_ModifiedById",
                schema: "public",
                table: "QuickBooksSettings",
                column: "ModifiedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuickBooksSettings",
                schema: "public");
        }
    }
}
