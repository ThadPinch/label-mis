using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemapJobStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remap the old JobStatus values to the new lifecycle:
            // old: Planned=0, Prepress=1, Scheduled=2, OnPress=3, Finishing=4, Qc=5, Packed=6, Shipped=7, Closed=8
            // new: PrePress=0, Queued=1, Printed=2, Finished=3, Shipped=4, Closed=5
            migrationBuilder.Sql("""
                UPDATE public."Job" SET "Status" = CASE "Status"
                    WHEN 0 THEN 0
                    WHEN 1 THEN 0
                    WHEN 2 THEN 1
                    WHEN 3 THEN 2
                    WHEN 4 THEN 2
                    WHEN 5 THEN 3
                    WHEN 6 THEN 3
                    WHEN 7 THEN 4
                    WHEN 8 THEN 5
                    ELSE 0 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse (merged values cannot be perfectly restored).
            migrationBuilder.Sql("""
                UPDATE public."Job" SET "Status" = CASE "Status"
                    WHEN 0 THEN 1
                    WHEN 1 THEN 2
                    WHEN 2 THEN 3
                    WHEN 3 THEN 5
                    WHEN 4 THEN 7
                    WHEN 5 THEN 8
                    ELSE 0 END;
                """);
        }
    }
}
