using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillEstimateLineData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // For each existing Estimate that hasn't already been migrated, create one
            // EstimateLine row carrying its spec data (LineNumber = 1).
            migrationBuilder.Sql(@"
                CREATE EXTENSION IF NOT EXISTS ""pgcrypto"";
                INSERT INTO public.""EstimateLine"" (
                    ""Id"", ""EstimateId"", ""LineNumber"", ""SourceProductId"",
                    ""ProductDescription"", ""LabelAcrossIn"", ""LabelAroundIn"", ""CornerRadiusIn"",
                    ""GutterAcrossIn"", ""GutterAroundIn"", ""BleedIn"", ""SubstrateId"",
                    ""InkSet"", ""WhiteInkUsed"", ""FinishingOperationsJson"",
                    ""SetupWasteImpressions"", ""RunningWastePct"", ""LineNotes"",
                    ""TenantId"", ""CreatedAt"", ""CreatedById"", ""ModifiedAt"", ""ModifiedById"")
                SELECT
                    gen_random_uuid(), e.""Id"", 1, NULL,
                    e.""ProductDescription"", e.""LabelAcrossIn"", e.""LabelAroundIn"", e.""CornerRadiusIn"",
                    e.""GutterAcrossIn"", e.""GutterAroundIn"", e.""BleedIn"", e.""SubstrateId"",
                    e.""InkSet"", e.""WhiteInkUsed"", e.""FinishingOperationsJson"",
                    e.""SetupWasteImpressions"", e.""RunningWastePct"", NULL,
                    e.""TenantId"", e.""CreatedAt"", e.""CreatedById"", e.""ModifiedAt"", e.""ModifiedById""
                FROM public.""Estimate"" e
                WHERE e.""SubstrateId"" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM public.""EstimateLine"" el WHERE el.""EstimateId"" = e.""Id""
                  );
            ");

            // Backfill EstimateQuantityBreak.EstimateLineId from the line just created.
            migrationBuilder.Sql(@"
                UPDATE public.""EstimateQuantityBreak"" b
                SET ""EstimateLineId"" = el.""Id""
                FROM public.""EstimateLine"" el
                WHERE el.""EstimateId"" = b.""EstimateId""
                  AND b.""EstimateLineId"" IS NULL;
            ");

            // Backfill Product.SourceEstimateLineId from the old SourceEstimateId column.
            migrationBuilder.Sql(@"
                UPDATE public.""Product"" p
                SET ""SourceEstimateLineId"" = el.""Id""
                FROM public.""EstimateLine"" el
                WHERE p.""SourceEstimateId"" IS NOT NULL
                  AND el.""EstimateId"" = p.""SourceEstimateId""
                  AND p.""SourceEstimateLineId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the backfill. Old data still lives in the legacy columns,
            // so we simply clear the new FK columns and drop the synthesized lines.
            migrationBuilder.Sql(@"UPDATE public.""Product"" SET ""SourceEstimateLineId"" = NULL;");
            migrationBuilder.Sql(@"UPDATE public.""EstimateQuantityBreak"" SET ""EstimateLineId"" = NULL;");
            migrationBuilder.Sql(@"DELETE FROM public.""EstimateLine"";");
        }
    }
}
