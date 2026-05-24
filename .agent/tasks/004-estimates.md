# Task 004: Estimates (Phase 3)

## Goal
Wire the estimating engine (task 002) into the UI. Estimators can create, edit, revise, save, and PDF estimates against real customer and master data (task 003).

## Context
- Read first: `AGENTS.md`
- Read first: `docs/tier1-buildout.md` Phase 3
- Read first: `docs/estimating-engine.md` — the engine spec already implemented in task 002
- Prerequisite: Tasks 001, 002, 003 merged

## Entities to add

### Estimate
- Id, TenantId, EstimateNumber (year-sequence: `EST-2026-00042`), CustomerId, SalesRepId, Status (Draft/Sent/Won/Lost/Expired enum), ProductDescription, LabelAcrossIn, LabelAroundIn, CornerRadiusIn, SubstrateId, InkSet, FinishingOperationsJson (JSONB), Notes, ValidUntilDate, WonAt, LostAt, LostReason, audit columns

### EstimateQuantityBreak
- EstimateId, Quantity, UnitPrice, TotalPrice, CalculatedCost, MarginPct, CostBreakdownJson (JSONB — the EstimateLineItem list from the engine)

### EstimateRevision
- EstimateId, RevisionNumber, SnapshotJson (full serialized estimate at this revision), CreatedAt, CreatedById
- Immutable: once written, never updated

## UI

### List page (`/estimates`)
- Filter by status, customer, sales rep, date range
- Search by estimate number, customer name, product description
- Columns: number, customer, product, status, total at highest qty break, created, valid until
- Sort by any column

### Create page (`/estimates/new`)
- Customer selector (typeahead, with "+ Add new" link to task-003 customer create page)
- Substrate selector showing width and cost-per-MSI
- Label dimensions (across, around, corner radius)
- Ink set dropdown (CMYK / CMYKW / CMYKW+spot / EPM)
- Finishing operations: pick from list, drag to reorder, set per-op overrides (setup time, run speed) if defaulting from master is wrong
- Quantity break editor: 3-5 rows of quantity, calculated unit price, calculated margin %
- Live recalculation as inputs change (call estimating engine on every blur)
- Validation: show engine warnings (yellow) and errors (red) inline
- Save as Draft → assigns estimate number
- Send to Customer → marks Sent, generates PDF, optionally emails (email is in scope, picking up `IEmailSender` registered in task 001)

### Edit page (`/estimates/{id}/edit`)
- Same as Create but loading the existing estimate
- Only editable if status = Draft
- If status = Sent/Won/Lost, show read-only with a "Create Revision" button

### Revision flow
- Clone existing estimate, bump RevisionNumber, set status = Draft
- Original estimate's snapshot is preserved in EstimateRevision table
- UI shows "Revision 2 of EST-2026-00042" at top
- All prior revisions accessible via dropdown

### PDF output
- QuestPDF template at `LabelsMis.Web/Pdf/EstimateTemplate.cs`
- Includes: shop logo, customer name+address, estimate number+date+valid-until, product description, quantity break table, terms (boilerplate text from settings), sales rep signature line
- Output saved to local disk under `/var/labels-mis/pdfs/estimates/` (configurable path) and linked from the estimate record
- Tier 2 will move this to S3; in Tier 1 local disk is fine

## Acceptance criteria
- [ ] All entities, configurations, migration in place (check before adding!)
- [ ] CRUD pages function end-to-end
- [ ] Live calculation works correctly — engine output displays in real time as inputs change
- [ ] PDF generation produces a usable quote document
- [ ] Revisions preserve history correctly (test: create estimate, revise it, original snapshot still accessible)
- [ ] Permissions: Estimator can create/edit drafts; Admin can change won/lost status; CSR read-only
- [ ] Domain test: estimate cannot transition from Won back to Draft without explicit revision
- [ ] Integration test: full flow from new estimate → sent → won persists correctly
- [ ] At least 3 manual estimates against real (or realistic) customer + substrate data match hand-calculated values to within 1 cent

## Out of scope
- Sending estimates via the actual email server (mock the IEmailSender in tests; real SMTP wiring is a config concern, not a code concern)
- Customer portal viewing of estimates (Tier 2)
- Comparing multiple substrates side-by-side on one estimate (Tier 2)
- Markup overrides per quantity break (Tier 2 — for now the markup is one number on the customer)
- Cost rollup across multiple jobs sharing an estimate (Tier 2)

## Deliverables
- PR with all entities, pages, PDF template, and tests
- Branch: `agent/004-estimates`

## Notes
- The estimate number sequence: use a Postgres sequence per year. Naive identity columns won't reset annually.
- Don't try to make the form clever about which fields to show — show them all and let the estimator decide. Cleverness goes in Tier 2.
- Live recalc should debounce 300ms on text inputs and fire immediately on dropdown changes. Use HTMX or vanilla JS fetch; do not bring in React.
