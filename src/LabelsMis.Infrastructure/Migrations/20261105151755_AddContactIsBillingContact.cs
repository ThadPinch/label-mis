using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContactIsBillingContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBillingContact",
                schema: "public",
                table: "Contact",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Until now a billing contact was one whose free-text Role read as billing. Tick the box
            // on exactly those contacts so no invoice that was Ready moves back to Waiting: the same
            // test the retired CustomerBillingRules.IsBillingRole ran — "accounts payable" anywhere,
            // or a word (split on space , ; - ( ) & .) of billing / bill(s) / invoice(s) / invoicing /
            // accounting / ap / a/p. Only rows still false are touched, so a re-run changes nothing.
            migrationBuilder.Sql("""
                UPDATE public."Contact" AS c
                SET "IsBillingContact" = true
                WHERE c."IsBillingContact" = false
                  AND c."Role" IS NOT NULL
                  AND (
                      lower(c."Role") LIKE '%accounts payable%'
                      OR EXISTS (
                          SELECT 1
                          FROM regexp_split_to_table(btrim(lower(c."Role"), E' \t\r\n\f'), '[ ,;()&.-]+') AS word
                          WHERE word IN ('billing', 'bill', 'bills', 'invoice', 'invoices', 'invoicing',
                                         'accounting', 'ap', 'a/p')
                      )
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBillingContact",
                schema: "public",
                table: "Contact");
        }
    }
}
