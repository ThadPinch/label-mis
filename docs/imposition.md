# Job imposition (pre-press step-and-repeat)

How a job's artwork becomes a press frame. Built 2026-08-17.

## Model

- `ImpositionTemplate` (`Domain/ValueObjects/ImpositionTemplate.cs`) — value object owned by `Job`
  (`Job.Imposition`, columns `Imposition*`, migration `AddJobImposition`, all nullable). Fields: label
  across/around, corner radius, gutters, bleed, labels across/around, `Orientation` (AsEntered /
  Rotated), `WebWidthIn`, `CrossWebOffsetIn`, `EyeMarks` (`ImpositionMarkSide`), eye-mark size,
  `IncludeDieLines`, `IncludeSlug`. Derived: `PlacedAcross/AroundIn`, `BlockWidthIn`,
  `RepeatLengthIn = around × (placedAround + gutterAround)`, margins, `OverflowsWeb`.
- `Job.ImposedArtworkFilePath` / `ImposedAt` / `ImposedFromArtworkFilePath` — the last generated
  frame and the artwork key it was built from (`ImposedArtworkIsStale(currentKey)`). The product's
  `ArtworkFilePath` stays the unimposed original.
- The template is **not** persisted at job creation. `JobImpositionService.GetAsync` returns the
  stored template or a computed default (`IsSeeded = true`); the first Save/Run/Reset persists it.

## Seeding (`JobImpositionService.BuildDefaultAsync`)

1. Die on the spec/product (`spec.DieId ?? product.DieId`): size, radius, gutters, labels across
   from the die; labels around = floor(press MaxRepeatIn / die pitch); orientation As entered.
2. Otherwise the estimating engine's `ImpositionResult` for the job spec (`JobService.ToEstimateLineInput`
   + `EstimateCalculationMapper.BuildRequestAsync` + `EstimatingService.Calculate`).
3. Otherwise a 1-up frame from the spec.
Web width = substrate roll width when it fits the press, else the press web width (Indigo 13.39").

## Generator (`Pdf/ImpositionPdfGenerator.cs`)

PdfPlatform (`PdfImporter.ImportPageAsForm` + `PdfCanvas.AddFormXObject`), no external libs.

- Page = `WebWidthIn × RepeatLengthIn` (points). Block centred + `CrossWebOffsetIn`; rows start
  half a gutter in from the bottom so consecutive frames tile with a full gutter.
- Source page: `/Rotate` honoured, TrimBox (fallback crop/media, with a warning) re-origined to (0,0).
  If the upright trim matches the placed label size (±0.03") it is placed as is; if the swapped
  dimensions match it is rotated 90° (`RotatedToFit`); otherwise placed unscaled + centred with a
  warning. The imported form's `/BBox` is widened to the media box (the importer uses the crop box,
  which would clip bleed) and `/Group` is copied.
- Each copy is clipped to trim + bleed, with the bleed between neighbours capped at the gutter so no
  copy paints over the next label's trim.
- Marks: eye marks (one per row, in the chosen margin, skipped with a warning when the margin is too
  narrow), die lines (rounded-rect trim outline, `CutContour` separation on an OCG "Die lines"),
  slug (job number, product, layout, size, repeat, web, timestamp — rotated along the web edge in
  whichever margin has room).
- Raster artwork (PNG/JPEG) is fitted to the trim only (no bleed). Encrypted / multi-page / repaired
  PDFs and non-PDF vector files raise or warn as appropriate.
- Tests: `tests/LabelsMis.Web.Tests/ImpositionPdfGeneratorTests.cs` (synthetic artwork with
  trim/bleed/rotate; set `PDF_SMOKE_OUT` to dump frames).

## Storage & UI

- Output key: `{ArtworkKeyPrefix}{productId}/imposed/{jobNumber}-{yyyyMMddHHmmss}.pdf` via
  `IFileStorageClient` (same bucket/prefix as artwork — never `tmp/pdfs/`, which is purged).
- Served by `/jobs/{id}/imposed-artwork[?inline=true]` (`Pages/Jobs/ImposedArtwork`).
- Job page: `_JobImpositionPanel` card (template form, Run / Save / Reset, warnings, expandable
  preview); handlers `SaveImposition` / `RunImposition` / `ResetImposition` on `Jobs/Detail`;
  `wwwroot/js/job-imposition.js` gives the live block/margin/repeat readout.
- Pre-press stage table has an Imposition column (`ProductionStageModel.ShowImpositionColumn`);
  the operator panel notes when the imposition is missing or stale; the job ticket prints the frame.

## Not done yet / ideas

- Multiple artwork files per job (versions/gangs) — the frame is one product's artwork.
- Per-press selection (everything assumes the Indigo 6800, like the estimator).
- Overprint on the die-line separation (PdfPlatform's canvas has no ExtGState op; would need `WriteRaw`).
