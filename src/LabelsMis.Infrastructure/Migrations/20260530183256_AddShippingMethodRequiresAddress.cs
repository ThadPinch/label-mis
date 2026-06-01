using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingMethodRequiresAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresAddress",
                schema: "public",
                table: "ShippingMethod",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // The seeded Pickup method does not need a ship-to address. The seeder won't
            // re-run on existing databases, so set it here for already-seeded installs.
            migrationBuilder.Sql(
                "UPDATE public.\"ShippingMethod\" SET \"RequiresAddress\" = false " +
                "WHERE \"Id\" = '22222222-2222-2222-2222-222222222201';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresAddress",
                schema: "public",
                table: "ShippingMethod");
        }
    }
}
