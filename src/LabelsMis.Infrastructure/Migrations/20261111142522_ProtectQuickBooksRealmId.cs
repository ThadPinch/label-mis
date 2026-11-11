using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProtectQuickBooksRealmId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The connected company's id (Intuit's realm id) was stored in the clear. From here it is
            // stored encrypted, like the tokens, as Intuit's security rules ask. SQL can't encrypt it
            // (the key is not in the database), so the column is renamed and widened and any value is
            // left as it is. No production database has a row: the connection had not shipped. A
            // development database that has one keeps a clear-text id the app can't read; connect
            // again there.
            migrationBuilder.RenameColumn(
                name: "RealmId",
                schema: "public",
                table: "QuickBooksConnection",
                newName: "RealmIdProtected");

            migrationBuilder.AlterColumn<string>(
                name: "RealmIdProtected",
                schema: "public",
                table: "QuickBooksConnection",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The build before this one expects the id in the clear, and SQL can't decrypt it. The
            // company link is removed (the same fields a disconnect clears), so that build starts
            // from "not connected" with the app keys still saved.
            migrationBuilder.Sql("""
                UPDATE public."QuickBooksConnection"
                SET "RealmIdProtected" = NULL, "CompanyName" = NULL,
                    "AccessTokenProtected" = NULL, "AccessTokenExpiresAt" = NULL,
                    "RefreshTokenProtected" = NULL, "RefreshTokenExpiresAt" = NULL, "RefreshTokenHardExpiresAt" = NULL,
                    "ConnectedAt" = NULL, "ConnectedById" = NULL, "LastRefreshedAt" = NULL,
                    "NeedsReconnect" = FALSE, "RefusedSince" = NULL, "LastError" = NULL, "LastErrorAt" = NULL,
                    "CompanyCheckJson" = NULL, "CompanyCheckedAt" = NULL
                WHERE "RealmIdProtected" IS NOT NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "RealmIdProtected",
                schema: "public",
                table: "QuickBooksConnection",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "RealmIdProtected",
                schema: "public",
                table: "QuickBooksConnection",
                newName: "RealmId");
        }
    }
}
