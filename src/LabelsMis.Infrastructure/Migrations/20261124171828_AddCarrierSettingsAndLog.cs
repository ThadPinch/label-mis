using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierSettingsAndLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierLog",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ErrorKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierLog_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierLog_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierSettings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientSecretProtected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ClientSecretTail = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ShipFromContact = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ShipFromPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DefaultServiceCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TrackingEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    BoxPresetsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastTestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BillToAccountNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BillToPostalCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierSettings_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierSettings_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierLog_CreatedAt",
                schema: "public",
                table: "CarrierLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierLog_CreatedById",
                schema: "public",
                table: "CarrierLog",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierLog_ModifiedById",
                schema: "public",
                table: "CarrierLog",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierLog_ShipmentId",
                schema: "public",
                table: "CarrierLog",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierSettings_CreatedById",
                schema: "public",
                table: "CarrierSettings",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierSettings_ModifiedById",
                schema: "public",
                table: "CarrierSettings",
                column: "ModifiedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierLog",
                schema: "public");

            migrationBuilder.DropTable(
                name: "CarrierSettings",
                schema: "public");
        }
    }
}
