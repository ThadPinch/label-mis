using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InkBottleCostAndMultiHit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WhiteHits",
                schema: "public",
                table: "EstimateLine",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SilverHits",
                schema: "public",
                table: "EstimateLine",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "WhiteCoveragePct",
                schema: "public",
                table: "EstimateLine",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "SilverCoveragePct",
                schema: "public",
                table: "EstimateLine",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            // Preserve existing white-ink selections as a single hit.
            migrationBuilder.Sql(
                "UPDATE public.\"EstimateLine\" SET \"WhiteHits\" = 1 WHERE \"WhiteInkUsed\" = true;");
            migrationBuilder.Sql(
                "UPDATE public.\"EstimateLine\" SET \"WhiteCoveragePct\" = 1, \"SilverCoveragePct\" = 1;");

            migrationBuilder.DropColumn(
                name: "WhiteInkUsed",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.AddColumn<decimal>(
                name: "BottleCost",
                schema: "public",
                table: "Ink",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BottleSizeMl",
                schema: "public",
                table: "Ink",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultCoveragePct",
                schema: "public",
                table: "Ink",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsSilver",
                schema: "public",
                table: "Ink",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MlPer1000SqIn",
                schema: "public",
                table: "Ink",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            // Default bottle sizes: colors 1500 mL, white 1800 mL; full coverage default.
            migrationBuilder.Sql(
                "UPDATE public.\"Ink\" SET \"BottleSizeMl\" = CASE WHEN \"IsWhite\" THEN 1800 ELSE 1500 END, \"DefaultCoveragePct\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BottleCost",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "BottleSizeMl",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "DefaultCoveragePct",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "IsSilver",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "MlPer1000SqIn",
                schema: "public",
                table: "Ink");

            migrationBuilder.DropColumn(
                name: "SilverCoveragePct",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "SilverHits",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "WhiteCoveragePct",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "WhiteHits",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.AddColumn<bool>(
                name: "WhiteInkUsed",
                schema: "public",
                table: "EstimateLine",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
