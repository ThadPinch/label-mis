using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingMethodNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "public",
                table: "ShippingMethod",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // FedEx's flat price is only an estimate once carrier cost × markup is billed.
            migrationBuilder.Sql("""
                UPDATE public."ShippingMethod"
                SET "Notes" = 'estimate — replaced by actual × markup at shipment'
                WHERE "Id" = '22222222-2222-2222-2222-222222222203'
                  AND "Notes" IS NULL;
                """);

            // The Customer account method (same fixed-Guid pattern as Pickup/Delivery/FedEx).
            // MasterDataSeeder only seeds an empty ShippingMethod table, so existing databases get
            // it here. Audit/tenant columns are borrowed from the oldest method row, so a fresh DB
            // (no rows yet, no users yet) is a no-op and the seeder inserts it with the defaults.
            // Skipped if the row, a method with that name (unique index), or a CustomerAccount-type
            // method already exists.
            migrationBuilder.Sql("""
                INSERT INTO public."ShippingMethod"
                    ("Id", "Name", "MethodType", "Price", "RequiresAddress", "Notes", "IsActive",
                     "TenantId", "CreatedAt", "CreatedById")
                SELECT '22222222-2222-2222-2222-222222222204', 'Customer account', 3, 0, TRUE,
                       'Knight Transportation — bills to customer, $0', TRUE,
                       src."TenantId", now(), src."CreatedById"
                FROM (SELECT "TenantId", "CreatedById"
                      FROM public."ShippingMethod"
                      ORDER BY "CreatedAt", "Id"
                      LIMIT 1) AS src
                WHERE NOT EXISTS (
                    SELECT 1 FROM public."ShippingMethod"
                    WHERE "Id" = '22222222-2222-2222-2222-222222222204'
                       OR lower(btrim("Name")) = 'customer account'
                       OR "MethodType" = 3);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the seeded Customer account row unless an order or estimate already uses it
            // (the FK is Restrict); a kept row is skipped by the next Up's NOT EXISTS guard.
            migrationBuilder.Sql("""
                DELETE FROM public."ShippingMethod" s
                WHERE s."Id" = '22222222-2222-2222-2222-222222222204'
                  AND NOT EXISTS (SELECT 1 FROM public."SalesOrder" o WHERE o."ShippingMethodId" = s."Id")
                  AND NOT EXISTS (SELECT 1 FROM public."Estimate" e WHERE e."ShippingMethodId" = s."Id");
                """);

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "public",
                table: "ShippingMethod");
        }
    }
}
