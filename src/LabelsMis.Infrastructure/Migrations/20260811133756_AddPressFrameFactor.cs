using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPressFrameFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing presses with the HP frame factor (2 clicks per impression
            // per separation) rather than 0, which the click calculator would clamp to 1.
            migrationBuilder.AddColumn<int>(
                name: "FrameFactor",
                schema: "public",
                table: "Press",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FrameFactor",
                schema: "public",
                table: "Press");
        }
    }
}
