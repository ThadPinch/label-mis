using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillPrimaryContactForBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Invoices go to the Billing contact, else the primary contact. Contacts added with the
            // form's Add contact saved Primary unticked even when it was ticked (the hidden "false"
            // field sat in front of the checkbox), so customers often have no primary contact. Where a
            // customer has no contact that can bill yet (no Billing or primary contact with an email)
            // and exactly one contact with an email, make that contact the primary. Customers with
            // several emailed contacts are left alone — which one gets invoices is a person's call.
            // Only rows still false are touched, so a re-run changes nothing.
            migrationBuilder.Sql("""
                UPDATE public."Contact" AS c
                SET "IsPrimary" = true
                WHERE c."IsPrimary" = false
                  AND nullif(btrim(c."Email"), '') IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM public."Contact" AS o
                      WHERE o."CustomerId" = c."CustomerId"
                        AND (o."IsBillingContact" OR o."IsPrimary")
                        AND nullif(btrim(o."Email"), '') IS NOT NULL)
                  AND (SELECT count(*)
                       FROM public."Contact" AS o
                       WHERE o."CustomerId" = c."CustomerId"
                         AND nullif(btrim(o."Email"), '') IS NOT NULL) = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data only: which contacts were primary before can't be told apart afterwards, and
            // unticking them would only send invoices back to Waiting. Nothing to undo.
        }
    }
}
