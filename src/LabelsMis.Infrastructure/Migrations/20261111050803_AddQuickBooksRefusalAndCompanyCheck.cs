using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickBooksRefusalAndCompanyCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyCheckJson",
                schema: "public",
                table: "QuickBooksConnection",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompanyCheckedAt",
                schema: "public",
                table: "QuickBooksConnection",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefusedSince",
                schema: "public",
                table: "QuickBooksConnection",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyCheckJson",
                schema: "public",
                table: "QuickBooksConnection");

            migrationBuilder.DropColumn(
                name: "CompanyCheckedAt",
                schema: "public",
                table: "QuickBooksConnection");

            migrationBuilder.DropColumn(
                name: "RefusedSince",
                schema: "public",
                table: "QuickBooksConnection");
        }
    }
}
