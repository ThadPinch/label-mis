using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPressFrameDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FrameRepeatIn",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxImageLengthIn",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxImageWidthIn",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxWebWidthIn",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinWebWidthIn",
                schema: "public",
                table: "Press",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE "Press"
                SET
                    "Name" = 'HP Indigo WS6800',
                    "WebWidthIn" = 13.39,
                    "MinWebWidthIn" = 7.87,
                    "MaxWebWidthIn" = 13.39,
                    "MaxImageWidthIn" = 12.59,
                    "FrameRepeatIn" = 18.9,
                    "MaxImageLengthIn" = 38.58,
                    "MaxRepeatIn" = 38.58,
                    "MinRepeatIn" = 18.9
                WHERE "Id" = '11111111-1111-1111-1111-111111111111';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FrameRepeatIn",
                schema: "public",
                table: "Press");

            migrationBuilder.DropColumn(
                name: "MaxImageLengthIn",
                schema: "public",
                table: "Press");

            migrationBuilder.DropColumn(
                name: "MaxImageWidthIn",
                schema: "public",
                table: "Press");

            migrationBuilder.DropColumn(
                name: "MaxWebWidthIn",
                schema: "public",
                table: "Press");

            migrationBuilder.DropColumn(
                name: "MinWebWidthIn",
                schema: "public",
                table: "Press");
        }
    }
}
