using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRewoundJobStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rewound is inserted at 4, between Finished(3) and Shipped. Shift existing
            // Shipped (was 4) and Closed (was 5) up by one to make room.
            migrationBuilder.Sql("""UPDATE public."Job" SET "Status" = "Status" + 1 WHERE "Status" >= 4;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the shift; any rows left in Rewound(4) collapse back to Finished's neighbour.
            migrationBuilder.Sql("""UPDATE public."Job" SET "Status" = "Status" - 1 WHERE "Status" >= 5;""");
        }
    }
}
