# Task 009: Invoicing and QuickBooks Export (Phase 8)

## Goal
Generate invoices on ship. Export to QuickBooks Online so the bookkeeper doesn't rekey. Track AR.

## Context
- Read first: `AGENTS.md`
- Read first: `docs/tier1-buildout.md` Phase 8
- Read first: `docs/domain-reference.md` section 7 (finance)
- Prerequisite: Tasks 001-008 merged

## Entities to add

### Invoice
- Id, TenantId, InvoiceNumber (`INV-2026-00042`), CustomerId, SalesOrderId, ShipmentId (nullable — sometimes invoice covers multiple shipments), InvoiceDate, DueDate (calculated from customer terms), Status (Draft/Sent/PartiallyPaid/Paid/Void enum), Subtotal, TaxAmount, ShippingAmount, Total, BalanceDue, QbExportedAt, QbInvoiceId (returned from QBO after export), Notes, audit columns

### InvoiceLine
- InvoiceId, LineNumber, SalesOrderLineId, JobId (nullable), Description (free text, defaults from product description), Quantity, UnitPrice, LineTotal, TaxCode (for QB mapping)

### Payment
- InvoiceId, PaymentDate, Amount, Method (Check/Ach/CreditCard/Wire/Other enum), Reference (check number, txn id), RecordedById, Notes
- Manual entry in Tier 1; bank-feed integration is Tier 3

## UI

### Invoice generation
- From shipment detail page: "Generate Invoice" button (visible when status = InTransit or Delivered)
- Pre-fills from shipment: customer, ship date, lines
- Calculates: subtotal from order lines, tax (use customer's tax-exempt flag — if exempt, $0; otherwise calculate via configured rate, hardcoded to a sane default in Tier 1), shipping (recovered from FedEx rate stored on shipment), total
- Save as Draft → Send (locks the invoice, generates PDF, optionally emails)

### Invoice list (`/invoices`)
- Filter by status, customer, date range, aging bucket (current / 1-30 / 31-60 / 61-90 / 90+)
- Sort by date, customer, total, balance due
- Quick actions: view PDF, record payment, export to QB

### Invoice detail (`/invoices/{id}`)
- All lines, totals, payment history
- Record payment form (date, amount, method, reference)
- Resend PDF
- Void invoice (admin only, requires reason)

### Invoice PDF
- QuestPDF template at `LabelsMis.Web/Pdf/InvoiceTemplate.cs`
- Shop letterhead, customer billing address, invoice number/date/due, line items, totals, payment instructions
- Saved to `/var/labels-mis/pdfs/invoices/` (configurable)

### AR aging report (`/reports/ar-aging`)
- Customer-by-customer breakdown of unpaid invoices grouped by aging bucket
- Total exposure
- CSV export

## QuickBooks Online export

Options ranked by what the bookkeeper likely uses:

1. **IIF file export** (oldest, ugliest, most universal — works for QB Desktop too)
2. **CSV with QBO column format** (works for QB Online import)
3. **Intuit QBO API** (cleanest, but requires OAuth + Intuit dev account)

Build option 2 (CSV) in Tier 1. Hooks for API in Tier 3.

### CSV export
- `/invoices/export?from={date}&to={date}` returns a CSV
- Columns match Intuit's invoice import spec: InvoiceNo, Customer, InvoiceDate, DueDate, Item, Description, Qty, Rate, Amount, TaxAmount, etc.
- One row per InvoiceLine
- After export, mark all included invoices with `QbExportedAt` timestamp
- Re-exporting already-exported invoices requires explicit confirmation

## Acceptance criteria
- [ ] All entities, configurations, migration in place
- [ ] Invoice generation from shipment works end-to-end
- [ ] Tax calculation respects customer tax-exempt flag
- [ ] Shipping cost recovery: invoice ShippingAmount = sum of FedEx rates stored on shipment packages
- [ ] PDF invoice renders cleanly and matches a reasonable accounting standard
- [ ] Payment recording reduces BalanceDue correctly; partial payment moves status to PartiallyPaid; full payment moves to Paid
- [ ] AR aging report buckets correctly
- [ ] QB CSV export produces a file that imports cleanly into QuickBooks Online (test against a sandbox QB account if available)
- [ ] Domain test: cannot void a Paid invoice
- [ ] Domain test: cannot record a payment greater than BalanceDue
- [ ] Permissions: Accounting role required for invoice creation, payment recording, voiding, exporting

## Out of scope
- Internal GL / chart of accounts (Tier 3 — for now QB owns the GL)
- AP / vendor invoice matching (Tier 2)
- Sales tax jurisdiction lookup by ship-to ZIP (Tier 2 — use a single rate from settings)
- Multi-currency (Tier 3)
- Credit memos / refunds (Tier 2)
- Statements (monthly customer statements showing all invoices) — Tier 2
- Recurring invoices / subscription billing (not relevant)
- Direct QBO API integration (Tier 3)
- Stripe / credit card payment processing (Tier 2 / 3)

## Deliverables
- PR with all entities, pages, PDF, CSV export, aging report
- Branch: `agent/009-invoicing`

## Notes
- The bookkeeper will define what "imports cleanly" means. Sit down with them before finalizing the CSV column set. Their workflow, not yours, defines success.
- Voiding invoices is destructive-feeling for the AR balance. Show a confirmation dialog with the impact ("This will reduce AR by $X"). Track who voided what and when.
- Aging buckets are universal (current, 30, 60, 90, 90+) but the day-of-month boundaries matter. Default: aging is calculated from InvoiceDate, not DueDate. Document this in the report header.
