using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateSentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                schema: "public",
                table: "Estimate",
                type: "timestamp with time zone",
                nullable: true);

            // Until now the only way past Draft was the send-to-customer step, so every estimate that
            // has left Draft (other than one cancelled outright) was sent — stamp it so history doesn't
            // read as "not sent". The exact send moment wasn't recorded; the last modification is the
            // closest available. EstimateStatus: Draft = 0, Cancelled = 5.
            migrationBuilder.Sql(
                """
                UPDATE public."Estimate"
                SET "SentAt" = COALESCE("ModifiedAt", "CreatedAt")
                WHERE "Status" NOT IN (0, 5);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SentAt",
                schema: "public",
                table: "Estimate");
        }
    }
}
