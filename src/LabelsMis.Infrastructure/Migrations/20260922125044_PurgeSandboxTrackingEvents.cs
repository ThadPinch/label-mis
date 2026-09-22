using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <summary>
    /// Data-only. The sandbox FedEx client fabricates three tracking events with fresh timestamps
    /// on every call, and the tracking poller ran it every 15 minutes against every open shipment,
    /// so production accumulated tens of thousands of fictitious TrackingEvent rows. This removes
    /// them. A row is sandbox junk only when it matches on every axis the stub controls: stamped by
    /// the system user (never a person), no carrier payload, and one of the stub's fixed
    /// description/location pairs. Real carrier events, once a real client exists, carry a payload
    /// and different text, so they are untouched. Not reversible: the rows carried no information.
    /// </summary>
    public partial class PurgeSandboxTrackingEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM public."TrackingEvent"
                WHERE "CreatedById" = '00000000-0000-0000-0000-000000000002'
                  AND "RawPayload" IS NULL
                  AND (
                        ("StatusDescription" = 'Label created'           AND "Location" = 'Origin facility')
                     OR ("StatusDescription" = 'In transit'              AND "Location" = 'Memphis, TN')
                     OR ("StatusDescription" = 'On vehicle for delivery' AND "Location" = 'Destination city')
                     OR ("StatusDescription" = 'Delivered'               AND "Location" = 'Destination city')
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the deleted rows were synthetic and carried no information.
        }
    }
}
