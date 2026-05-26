# Tier 1 — Buildout Breakdown

Target: HP Indigo 6800 narrow-web label shop, 6 users, evenings/weekends pace.
Realistic timeline: 8-14 weeks if disciplined about scope.

This document is the build order. Each phase is independently shippable — you can stop at the end of any phase and have something usable, even if incomplete. Do not start phase N+1 until phase N has been used to run a real job.

**Implementation status (May 2026):** Phases 0–8 are implemented in code (master data through invoicing). Phase 9 is operational (training, parallel run, cutover) — see `docs/runbooks/cutover-checklist.md` and `src/LabelsMis.Tools/` for CSV importers. System architecture: `docs/architecture.md`. Entity relationships: `docs/schema-erd.md`.

---

## Phase 0 — Project scaffold (week 0, ~2 evenings)

**Goal**: Empty .NET app with database, auth, deployment pipeline, and CI working before any business logic exists.

- `.NET 10` solution, four projects:
  - `LabelsMis.Domain` — entities, value objects, business rules (no EF dependency)
  - `LabelsMis.Infrastructure` — EF DbContext, migrations, external service clients (FedEx, email)
  - `LabelsMis.Web` — Razor Pages UI, application services, PDF generation, background workers
  - `LabelsMis.Tools` — CLI CSV importers for cutover (references Infrastructure only)
- Postgres database, EF Core code-first migrations
- ASP.NET Identity for auth, seeded admin user
- `.github/workflows/ci.yml` — build + test + EF migrations check on every PR
- `docker-compose.yml` for local Postgres
- `AGENTS.md` at root, `.agent/tasks/` for specs, `docs/` for reference
- Deployment target chosen (EC2 / Render / Railway / Fly — pick one and stop deliberating)

**Done when**: you can `git pull && docker-compose up && dotnet run`, log in as admin, see an empty dashboard, and push a PR that runs CI green.

---

## Phase 1 — Master data (weeks 1-3)

**Goal**: All the reference data the estimating engine needs is enterable through the UI before you build the estimating engine itself.

### Customers
- `Customer` (name, code, terms, tax_exempt, default_markup_pct, status, sales_rep_id)
- `Address` (customer_id, type [billing/shipping], street, city, state, zip, country)
- `Contact` (customer_id, name, email, phone, role)
- CRUD screens, search by name/code, address-edit-inline

### Suppliers
- `Supplier` (name, code, terms, default_lead_time_days, account_number)
- `SupplierContact` (same shape as customer contact)

### Substrates (stocks)
- `Stock` (code, description, face_material, adhesive, liner, total_caliper_mil, width_in, supplier_id, supplier_part_number, cost_per_msi, msi_to_lf_factor, min_order_qty, is_active)
- Cost history: `StockCostHistory` (stock_id, cost_per_msi, effective_date)

### Dies
- `Die` (description, customer_id [nullable — shop-owned vs customer-owned], die_type [flexible/solid/sheeter], shape, label_across_in, label_around_in, corner_radius_in, gutter_across_in, gutter_around_in, labels_across, labels_around, repeat_length_in, web_width_in, supplier_id, supplier_part_number, location, last_used_at, usage_count, retired_at)
- Usage history: `DieUsage` (die_id, job_id, used_at, used_by)

### Inks (Indigo)
- `Ink` (code, description, ink_set [CMYK/CMYKW/CMYKW+spot/EPM], click_rate_per_1000, is_active)
- Keep this simple in v1 — HP publishes the click rates per ink set per substrate class. Five to ten rows total.

### Finishing operations
- `FinishingOperation` (code, description, type [laminate/varnish/diecut/slit/sheet/foil/emboss/perf], setup_minutes, run_speed_fpm, equipment_name, cost_per_hour)
- These are the cost components for off-line finishing. Start with: rotary die-cut + matrix strip, laminate, slit, varnish.

### Presses
- `Press` (name, code, type [digital_toner/digital_inkjet/flexo/hybrid], web_width_in, max_repeat_in, min_repeat_in, max_colors, speed_fpm, setup_minutes, cost_per_hour, click_based [bool], is_active)
- Seed one row: Indigo 6800. Schema supports adding more later without migration.

**Done when**: an estimator can sit down and add a new customer with a substrate from a new supplier and a customer-owned die without leaving the app.

---

## Phase 2 — Estimating engine (weeks 3-5)

**Goal**: A pure-function calculation library with unit tests, callable from the UI to produce quote pricing.

This is the heart of the system. Build it as a standalone library (`LabelsMis.Domain/Estimating/`) with no UI, no EF, no database. It takes structured input and returns structured output. See `docs/estimating-engine.md` for the full spec.

Deliverables:
- `EstimatingService.Calculate(EstimateRequest) → EstimateResult`
- Unit tests for 10+ scenarios (different ink sets, with/without lamination, EPM mode, quantity breaks)
- Manually verified against 5+ historical jobs from the shop (input parameters → expected price within 2%)

**Done when**: the test suite passes and you've compared output to real historical Indigo jobs.

---

## Phase 3 — Estimates (weeks 5-7)

**Goal**: Estimator can create, save, revise, and PDF a quote.

### Schema
- `Estimate` (id, customer_id, sales_rep_id, status [draft/sent/won/lost/expired], product_description, label_across_in, label_around_in, corner_radius_in, substrate_id, ink_set, finishing_ops [JSONB list of finishing_operation_id with config], notes, valid_until, created_at, created_by, won_at, lost_reason)
- `EstimateQuantityBreak` (estimate_id, quantity, unit_price, total_price, calculated_cost, margin_pct) — typically 3-5 rows per estimate (1k/5k/10k/25k/50k)
- `EstimateRevision` (estimate_id, revision_number, snapshot_json, created_at, created_by) — immutable snapshots so revised quotes don't overwrite history

### UI
- Estimate form with live recalculation as inputs change
- Customer selector with quick-add
- Substrate selector showing cost and width
- Quantity break editor (add/remove tiers)
- Preview pane with margins visible to estimator only
- "Send to customer" → generates PDF, marks status sent, optionally emails
- Revision: clone existing estimate with bumped revision number

### PDF output
- QuestPDF or similar — clean quote layout
- Customer logo, shop logo, line items, quantity breaks, terms, valid-until date
- File saved to S3 / disk, link on estimate record

**Done when**: an estimator creates an estimate end-to-end, customer receives a PDF, comes back asking for a different quantity → revision works without losing the original.

---

## Phase 4 — Products and orders (weeks 7-8)

**Goal**: Won estimates become products (reusable specs). Customer POs become sales orders ready for production scheduling (jobs are created in Phase 5).

### Products
- `Product` (id, customer_id, customer_sku, internal_sku, description, source_estimate_id, label_across_in, label_around_in, substrate_id, ink_set, finishing_ops, die_id, roll_spec_id, artwork_file_path, status [active/discontinued], created_at)
- `RollSpec` (product_id, labels_per_roll, core_size_in, unwind_position [1-8], max_od_in, rolls_per_case)
- Auto-create product when an estimate is marked Won
- Subsequent orders reference product, not estimate

### Sales orders
- `SalesOrder` (id, customer_id, customer_po_number, ordered_at, requested_ship_date, status [open/in_production/shipped/invoiced/closed], notes, created_by)
- `SalesOrderLine` (sales_order_id, product_id, quantity, unit_price, line_notes)
- Order entry screen: select customer → select their products → set quantities → save

**Done when**: a customer places a repeat order against an existing product without going through estimating again, and the order shows up as ready to schedule.

---

## Phase 5 — Jobs and production (weeks 8-10)

**Goal**: Sales orders become job tickets that follow the work through the shop.

### Schema
- `Job` (id, job_number [year-sequence like 2026-00472], sales_order_line_id, product_id, quantity_ordered, quantity_planned [with overrun %], status [planned/prepress/scheduled/on_press/finishing/qc/packed/shipped/closed], scheduled_for_date, due_date, priority, notes, created_at)
- `JobOperation` (job_id, sequence, operation_type [press/finishing/inspection/pack/ship], equipment_id, planned_start_at, planned_minutes, actual_start_at, actual_end_at, status, operator_id, good_count, waste_count, downtime_minutes)
- `JobMaterialUsage` (job_id, stock_id, roll_id [nullable], quantity_used_lf, used_at, used_by) — roll linkage added in Phase 6
- `JobTimeEntry` (job_operation_id, user_id, clocked_in_at, clocked_out_at) — for accurate labor cost

### UI
- Job ticket print view (the paper that follows the work — barcode the job number)
- Open jobs list, filterable by status / press / due date
- Job detail page showing all operations and actuals vs planned
- Operator screen (tablet-friendly): show me jobs assigned to my press today, clock-on, enter counts, clock-off
- Simple scheduling: drag-drop or just date+press assignment (no gantt yet)

**Done when**: a job runs through the shop, every operation gets clocked, and at the end of the job you can see actual cost vs estimated cost.

---

## Phase 6 — Inventory (weeks 10-11)

**Goal**: Know what raw stock you have, where it is, and what's been consumed.

### Schema
- `PurchaseOrder` (id, po_number, supplier_id, ordered_at, expected_at, status, created_by)
- `PurchaseOrderLine` (po_id, stock_id, quantity_lf, unit_cost, line_total)
- `Receipt` (po_line_id, received_at, received_by, quantity_lf, notes)
- `Roll` (id, roll_barcode, stock_id, supplier_lot_number, width_in, original_length_lf, remaining_length_lf, received_at, location, status [available/staged/on_press/depleted/scrapped], notes)
- `RollMovement` (roll_id, movement_type [receive/stage/consume/split/scrap/return], quantity_lf, job_id [nullable], moved_at, moved_by)

### UI
- PO entry / receipt
- Roll receipt: scan/enter barcode, lot, length → roll record created
- Roll search by barcode, stock, location
- Roll splitting (cut a 13" roll into two 6.5" rolls → original consumed, two new rolls created)
- Manual roll consumption (link to job)

**Done when**: every roll in the building has a barcode, you can scan it and see what's left, and at month-end the system count matches a physical count within 2%.

---

## Phase 7 — Shipping (week 11-12)

**Goal**: Generate FedEx labels, capture tracking, notify customer.

### Schema
- `Shipment` (id, sales_order_id, ship_date, carrier, service_level, ship_from_address_id, ship_to_address_id, status, created_by)
- `ShipmentPackage` (shipment_id, weight_lb, length_in, width_in, height_in, tracking_number, label_url, declared_value)
- `ShipmentLine` (shipment_id, sales_order_line_id, quantity_shipped) — partial shipments allowed
- `TrackingEvent` (shipment_package_id, event_at, status, location, raw_payload)

### FedEx integration
- Reuse FedEx API patterns you already have from OMS work (ZPL label printing via QZ Tray)
- Rate quote endpoint for shipping cost recovery
- Label generation endpoint
- Webhook receiver for tracking updates (or polling fallback)
- Customer email on ship with tracking link

**Done when**: a packer can pick a job, weigh it, generate and print a FedEx label, and the customer receives a tracking email automatically.

---

## Phase 8 — Invoicing and QB export (weeks 12-13)

**Goal**: Generate invoices on ship, export to QuickBooks.

### Schema
- `Invoice` (id, invoice_number, customer_id, sales_order_id, shipment_id, invoice_date, due_date, status [draft/sent/partially_paid/paid/void], subtotal, tax, shipping, total, balance_due, qb_export_at)
- `InvoiceLine` (invoice_id, sales_order_line_id, description, quantity, unit_price, line_total)
- `Payment` (invoice_id, payment_date, amount, method, reference) — manual entry only in v1

### UI
- Generate invoice from shipment (one-click, pre-filled)
- Invoice PDF (QuestPDF)
- AR aging report (current / 30 / 60 / 90+)
- QuickBooks Online export — **CSV import format** in Tier 1 (`/invoices/export`); direct QBO API deferred to Tier 3

**Done when**: invoices generate on ship, bookkeeper imports to QB without manual rekeying.

---

## Phase 9 — Polish and cutover (weeks 13-14)

**Goal**: Replace whatever the shop is using today.

- Bulk import via `LabelsMis.Tools` CLI: customers, stocks, products, opening AR balances (see `docs/runbooks/cutover-checklist.md`)
- User training (6 people, 30 min each role-specific)
- Run new system parallel to old for 2 weeks
- Cutover — disaster recovery: `docs/runbooks/disaster-recovery.md`

---

## Cross-cutting concerns (build into every phase)

### Auth & roles
Roles seeded in phase 0, used from phase 1 onward:
- **Admin** — full access, system config
- **Estimator** — customers, estimates, products, view jobs
- **CSR** — customers, sales orders, view estimates
- **Scheduler** — jobs, scheduling, view inventory
- **Operator** — assigned jobs only, clock on/off, count entry
- **Shipping** — shipments, view orders, generate labels
- **Accounting** — invoices, payments, QB export, view everything

### Audit trail
Every business entity table has: `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById` (via `EntityBase`). A dedicated `AuditLog` table for sensitive operations (price changes, void invoices, manual roll adjustments) is **deferred to Tier 2** — void reasons and payment records capture the essentials for now.

### Soft delete
`Customer`, `Product`, `Stock`, `Die`, `Supplier` — soft delete (set `is_active=false`), never hard delete. Transactional records (estimates, orders, invoices) are immutable once non-draft.

### Multi-tenant shape
Even though v1 is single-tenant, every table that holds business data gets a `tenant_id` (or `shop_id`) column with a default value. Costs nothing now and saves a 6-month migration later if the system gets used by another shop.

### Currency and units
- Money: `decimal(18,4)`, never float
- Units: store everything in inches and linear feet internally; display per user preference
- Imperial-only is fine for v1 (US shop); metric conversion is tier 3

---

## What does NOT belong in tier 1

If you find yourself building any of these, stop and put it in tier 2:

- Scheduling gantt (manual date+press assignment is enough)
- Customer portal
- Prepress (JDF / Esko) integration
- HP PrintOS integration
- Real QC module (a `notes` field on the job is enough)
- AP / vendor invoice matching (manual)
- Multi-site
- Internal GL (export to QB instead)
- BI / dashboards (Postgres views + Metabase if you really need it)
- Mobile apps (responsive web is fine)
- EDI
- AI estimating
- Variable data / versioning
- Combination / gang jobs

---

## File / folder layout

```
labels-mis/
├── AGENTS.md
├── README.md
├── docker-compose.yml
├── global.json
├── Directory.Packages.props
├── .agent/tasks/                 ← work orders (001–010)
├── docs/
│   ├── architecture.md           ← stack diagram, layer rules
│   ├── domain-reference.md       ← industry terminology
│   ├── estimating-engine.md      ← calculation engine spec
│   ├── schema-erd.md             ← entity relationship diagram
│   ├── tier1-buildout.md         ← this file
│   └── runbooks/
│       ├── cutover-checklist.md
│       └── disaster-recovery.md
├── src/
│   ├── LabelsMis.Domain/
│   │   ├── Common/               ← EntityBase, TenantConstants
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Estimating/           ← pure calculation engine
│   │   ├── Fedex/                ← IFedexClient interface
│   │   ├── Email/                ← IEmailSender interface
│   │   ├── Jobs/                 ← JobCostCalculator
│   │   └── Inventory/            ← roll split / reconciliation rules
│   ├── LabelsMis.Infrastructure/
│   │   ├── Persistence/          ← DbContext, configurations, seeders
│   │   ├── Migrations/
│   │   ├── Fedex/                ← SandboxFedexClient (+ prod client Tier 2)
│   │   ├── Email/                ← LoggingEmailSender
│   │   └── Identity/
│   ├── LabelsMis.Web/
│   │   ├── Pages/                ← Razor Pages by area (see below)
│   │   ├── Services/             ← application orchestration
│   │   ├── Pdf/                  ← QuestPDF templates (estimate, job ticket, invoice)
│   │   ├── Background/           ← ShipmentTrackingPoller
│   │   ├── Authorization/
│   │   └── wwwroot/
│   └── LabelsMis.Tools/
│       └── Importers/            ← CSV cutover importers
├── tests/
│   ├── LabelsMis.Domain.Tests/
│   ├── LabelsMis.Infrastructure.Tests/
│   └── LabelsMis.Web.Tests/
└── .github/workflows/ci.yml
```

### Razor Pages areas (implemented)

| Area | Routes |
|------|--------|
| Master data | `/customers`, `/suppliers`, `/stocks`, `/dies`, `/inks`, `/finishing-operations`, `/presses` |
| Estimating | `/estimates`, `/estimates/new`, `/estimates/{id}/edit` |
| Products & orders | `/products`, `/sales-orders` |
| Production | `/jobs`, `/jobs/{id}`, `/jobs/{id}/ticket`, `/operator/job/{jobNumber}` |
| Inventory | `/purchase-orders`, `/rolls` |
| Shipping | `/shipments` |
| Invoicing | `/invoices`, `/reports/ar-aging` |
