using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageSettings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BucketName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SecretKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ArtworkKeyPrefix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UseLocalFallback = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageSettings_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageSettings_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorageSettings_CreatedById",
                schema: "public",
                table: "StorageSettings",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_StorageSettings_ModifiedById",
                schema: "public",
                table: "StorageSettings",
                column: "ModifiedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageSettings",
                schema: "public");
        }
    }
}
