using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPressSeparationSpeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SpeedFpm3Separations",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpeedFpm4Separations",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpeedFpm5PlusSeparations",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            // The HP Indigo WS6800 runs one separation per cylinder revolution: 131 fpm at 3
            // separations (EPM), 98 at 4 (CMYK, EPMW + 1 white), 72 at 5+ (CMYKW + 1 white).
            // Only the seeded WS6800 row (the press every estimate prices on) is touched, only
            // while all three speeds are still blank; no row matched = no-op (e.g. a fresh DB,
            // where the press is seeded after migrations with these speeds already set).
            migrationBuilder.Sql("""
                UPDATE public."Press"
                SET "SpeedFpm3Separations" = 131,
                    "SpeedFpm4Separations" = 98,
                    "SpeedFpm5PlusSeparations" = 72
                WHERE "Id" = '11111111-1111-1111-1111-111111111111'
                  AND "SpeedFpm3Separations" IS NULL
                  AND "SpeedFpm4Separations" IS NULL
                  AND "SpeedFpm5PlusSeparations" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeedFpm3Separations",
                schema: "public",
                table: "Press");

            migrationBuilder.DropColumn(
                name: "SpeedFpm4Separations",
                schema: "public",
                table: "Press");

            migrationBuilder.DropColumn(
                name: "SpeedFpm5PlusSeparations",
                schema: "public",
                table: "Press");
        }
    }
}
