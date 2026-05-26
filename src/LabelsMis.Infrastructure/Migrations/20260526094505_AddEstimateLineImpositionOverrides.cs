using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateLineImpositionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LabelOrientationOverride",
                schema: "public",
                table: "EstimateLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLabelsAcrossOverride",
                schema: "public",
                table: "EstimateLine",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LabelOrientationOverride",
                schema: "public",
                table: "EstimateLine");

            migrationBuilder.DropColumn(
                name: "MaxLabelsAcrossOverride",
                schema: "public",
                table: "EstimateLine");
        }
    }
}
