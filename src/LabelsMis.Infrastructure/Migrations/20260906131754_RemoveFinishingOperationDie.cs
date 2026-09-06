using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFinishingOperationDie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinishingOperation_Die_DieId",
                schema: "public",
                table: "FinishingOperation");

            migrationBuilder.DropIndex(
                name: "IX_FinishingOperation_DieId",
                schema: "public",
                table: "FinishingOperation");

            migrationBuilder.DropColumn(
                name: "DieId",
                schema: "public",
                table: "FinishingOperation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DieId",
                schema: "public",
                table: "FinishingOperation",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinishingOperation_DieId",
                schema: "public",
                table: "FinishingOperation",
                column: "DieId");

            migrationBuilder.AddForeignKey(
                name: "FK_FinishingOperation_Die_DieId",
                schema: "public",
                table: "FinishingOperation",
                column: "DieId",
                principalSchema: "public",
                principalTable: "Die",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
