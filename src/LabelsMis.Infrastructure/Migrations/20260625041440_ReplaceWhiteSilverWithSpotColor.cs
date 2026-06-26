using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceWhiteSilverWithSpotColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep existing white inks: IsWhite (bool) becomes the new IsSpot flag.
            migrationBuilder.RenameColumn(
                name: "IsWhite",
                schema: "public",
                table: "Ink",
                newName: "IsSpot");

            migrationBuilder.AddColumn<int>(
                name: "SpotColor",
                schema: "public",
                table: "Ink",
                type: "integer",
                nullable: true);

            // Former white inks (IsSpot already true via the rename) get SpotColor = White (0).
            migrationBuilder.Sql(
                "UPDATE public.\"Ink\" SET \"SpotColor\" = 0 WHERE \"IsSpot\" = true;");

            // Former silver inks become spot inks with SpotColor = Silver (1).
            migrationBuilder.Sql(
                "UPDATE public.\"Ink\" SET \"IsSpot\" = true, \"SpotColor\" = 1 WHERE \"IsSilver\" = true;");

            migrationBuilder.AddColumn<string>(
                name: "SpotsJson",
                schema: "public",
                table: "EstimateLine",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            // Best-effort: carry forward existing silver selections into the spot collection,
            // matching the SpotSelectionInput DTO shape (InkId, Hits, CoveragePct, SortOrder).
            migrationBuilder.Sql(
                """
                UPDATE public."EstimateLine" el
                SET "SpotsJson" = jsonb_build_array(jsonb_build_object(
                    'InkId', (SELECT i."Id" FROM public."Ink" i WHERE i."SpotColor" = 1 ORDER BY i."Code" LIMIT 1),
                    'Hits', el."SilverHits",
                    'CoveragePct', el."SilverCoveragePct",
                    'SortOrder', 0))
                WHERE el."SilverHits" > 0
                  AND EXISTS (SELECT 1 FROM public."Ink" i WHERE i."SpotColor" = 1);
                """);

            migrationBuilder.DropColumn(
                name: "IsSilver",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "SilverHits",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "SilverCoveragePct",
                schema: "public",
                table: "EstimateLine");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSilver",
                schema: "public",
                table: "Ink",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SilverHits",
                schema: "public",
                table: "EstimateLine",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SilverCoveragePct",
                schema: "public",
                table: "EstimateLine",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Restore the silver flag from the spot color.
            migrationBuilder.Sql(
                "UPDATE public.\"Ink\" SET \"IsSilver\" = true WHERE \"SpotColor\" = 1;");

            // Only white inks may keep the IsSpot/IsWhite flag after the rename below;
            // every other spot color is not representable in the old schema.
            migrationBuilder.Sql(
                "UPDATE public.\"Ink\" SET \"IsSpot\" = false WHERE \"SpotColor\" IS DISTINCT FROM 0;");

            migrationBuilder.DropColumn(
                name: "SpotsJson",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "SpotColor",
                schema: "public",
                table: "Ink");

            migrationBuilder.RenameColumn(
                name: "IsSpot",
                schema: "public",
                table: "Ink",
                newName: "IsWhite");
        }
    }
}
