using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickBooksSyncLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuickBooksSyncLog",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuickBooksId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IntuitTid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickBooksSyncLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuickBooksSyncLog_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuickBooksSyncLog_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksSyncLog_CreatedAt",
                schema: "public",
                table: "QuickBooksSyncLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksSyncLog_CreatedById",
                schema: "public",
                table: "QuickBooksSyncLog",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksSyncLog_InvoiceId",
                schema: "public",
                table: "QuickBooksSyncLog",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_QuickBooksSyncLog_ModifiedById",
                schema: "public",
                table: "QuickBooksSyncLog",
                column: "ModifiedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuickBooksSyncLog",
                schema: "public");
        }
    }
}
