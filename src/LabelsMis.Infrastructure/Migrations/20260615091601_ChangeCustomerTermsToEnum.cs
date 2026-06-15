using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeCustomerTermsToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert the free-text Terms column into the PaymentTerms enum, whose integer value is
            // the number of days until due (COD = 0, Net 15 = 15, Net 30 = 30, Net 60 = 60).
            migrationBuilder.Sql(@"
                ALTER TABLE public.""Customer""
                ALTER COLUMN ""Terms"" TYPE integer
                USING (
                    CASE
                        WHEN upper(btrim(""Terms"")) = 'COD' THEN 0
                        WHEN ""Terms"" LIKE '%15%' THEN 15
                        WHEN ""Terms"" LIKE '%60%' THEN 60
                        ELSE 30
                    END
                );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE public.""Customer""
                ALTER COLUMN ""Terms"" TYPE character varying(100)
                USING (
                    CASE ""Terms""
                        WHEN 0 THEN 'COD'
                        WHEN 15 THEN 'Net 15'
                        WHEN 60 THEN 'Net 60'
                        ELSE 'Net 30'
                    END
                );");
        }
    }
}
