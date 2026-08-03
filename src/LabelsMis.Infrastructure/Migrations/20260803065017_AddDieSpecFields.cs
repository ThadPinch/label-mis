using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDieSpecFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DieRepeatIn",
                schema: "public",
                table: "Die",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinerSpec",
                schema: "public",
                table: "Die",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SetupRating",
                schema: "public",
                table: "Die",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpeedRating",
                schema: "public",
                table: "Die",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DieRepeatIn",
                schema: "public",
                table: "Die");

            migrationBuilder.DropColumn(
                name: "LinerSpec",
                schema: "public",
                table: "Die");

            migrationBuilder.DropColumn(
                name: "SetupRating",
                schema: "public",
                table: "Die");

            migrationBuilder.DropColumn(
                name: "SpeedRating",
                schema: "public",
                table: "Die");
        }
    }
}
