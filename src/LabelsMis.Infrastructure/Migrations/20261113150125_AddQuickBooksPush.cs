using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickBooksPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PoCustomFieldId",
                schema: "public",
                table: "QuickBooksSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PoCustomFieldName",
                schema: "public",
                table: "QuickBooksSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SendMode",
                schema: "public",
                table: "QuickBooksSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TermsMapJson",
                schema: "public",
                table: "QuickBooksSettings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QbAttemptCount",
                schema: "public",
                table: "Invoice",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QbExportStatus",
                schema: "public",
                table: "Invoice",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "QbHoldSend",
                schema: "public",
                table: "Invoice",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "QbLastAttemptAt",
                schema: "public",
                table: "Invoice",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QbLastError",
                schema: "public",
                table: "Invoice",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QbLastErrorCode",
                schema: "public",
                table: "Invoice",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QbNextAttemptAt",
                schema: "public",
                table: "Invoice",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QbPayloadHash",
                schema: "public",
                table: "Invoice",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QbQueuedAt",
                schema: "public",
                table: "Invoice",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QbQueuedById",
                schema: "public",
                table: "Invoice",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QbSendToOverride",
                schema: "public",
                table: "Invoice",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QbSyncToken",
                schema: "public",
                table: "Invoice",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QbTax",
                schema: "public",
                table: "Invoice",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QbTotal",
                schema: "public",
                table: "Invoice",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QbVoidedAt",
                schema: "public",
                table: "Invoice",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuickBooksCustomerName",
                schema: "public",
                table: "Customer",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuickBooksCatalogEntry",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    QuickBooksId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FullName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Taxable = table.Column<bool>(type: "boolean", nullable: true),
                    RefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickBooksCatalogEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuickBooksCatalogEntry_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuickBooksCatalogEntry_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_QbExportStatus",
                schema: "public",
                table: "Invoice",
                column: "QbExportStatus");

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksCatalogEntry_CreatedById",
                schema: "public",
                table: "QuickBooksCatalogEntry",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksCatalogEntry_Kind_QuickBooksId",
                schema: "public",
                table: "QuickBooksCatalogEntry",
                columns: new[] { "Kind", "QuickBooksId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksCatalogEntry_ModifiedById",
                schema: "public",
                table: "QuickBooksCatalogEntry",
                column: "ModifiedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuickBooksCatalogEntry",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_QbExportStatus",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PoCustomFieldId",
                schema: "public",
                table: "QuickBooksSettings");

            migrationBuilder.DropColumn(
                name: "PoCustomFieldName",
                schema: "public",
                table: "QuickBooksSettings");

            migrationBuilder.DropColumn(
                name: "SendMode",
                schema: "public",
                table: "QuickBooksSettings");

            migrationBuilder.DropColumn(
                name: "TermsMapJson",
                schema: "public",
                table: "QuickBooksSettings");

            migrationBuilder.DropColumn(
                name: "QbAttemptCount",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbExportStatus",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbHoldSend",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbLastAttemptAt",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbLastError",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbLastErrorCode",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbNextAttemptAt",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbPayloadHash",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbQueuedAt",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbQueuedById",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbSendToOverride",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbSyncToken",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbTax",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbTotal",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QbVoidedAt",
                schema: "public",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "QuickBooksCustomerName",
                schema: "public",
                table: "Customer");
        }
    }
}
