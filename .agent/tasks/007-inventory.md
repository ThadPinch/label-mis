# Task 007: Inventory (Phase 6)

## Goal
Track every roll of stock in the building from receipt to consumption. Roll-level barcode traceability with lot/batch numbers for FDA/pharma customers.

## Context
- Read first: `AGENTS.md`
- Read first: `docs/tier1-buildout.md` Phase 6
- Read first: `docs/domain-reference.md` section 5 (inventory)
- Prerequisite: Tasks 001-006 merged

## Entities to add

### PurchaseOrder
- Id, TenantId, PoNumber (`PO-2026-00042`), SupplierId, OrderedAt, ExpectedAt, Status (Draft/Sent/PartiallyReceived/Received/Cancelled enum), Notes, audit columns

### PurchaseOrderLine
- PoId, LineNumber, StockId, QuantityLf, UnitCost, LineTotal, QuantityReceivedLf

### Receipt
- PoLineId, ReceivedAt, ReceivedById, QuantityLf, Notes
- One PO line may have multiple receipts (partial deliveries)

### Roll
- Id, TenantId, RollBarcode (unique, indexed — printed on the roll on receipt), StockId, SupplierLotNumber, WidthIn (may differ from Stock's master width if slit at supplier), OriginalLengthLf, RemainingLengthLf, ReceivedAt, ReceiptId (FK back to Receipt), Location (free text — rack/zone), Status (Available/Staged/OnPress/Depleted/Scrapped enum), Notes, audit columns

### RollMovement
- RollId, MovementType (Receive/Stage/Consume/Split/Scrap/Return enum), QuantityLf (signed: negative for consumption), JobId (nullable — null for non-job movements like scrap), MovedAt, MovedById, Notes
- Append-only ledger; never updated

### Update JobMaterialUsage (from task 006)
- Add RollId column (nullable until backfilled)
- Now when an operator consumes material, they specify which roll → RollMovement is auto-generated

## UI

### PO entry (`/purchase-orders`)
- List page with filters
- New PO: select supplier, add lines (stock + quantity + unit cost defaults from Stock master), save as Draft → Send (changes status)
- Receipt entry: open PO, "Receive" button → for each line enter quantity received + supplier lot number → creates Roll records (one Roll per physical roll received; if a PO line for 30,000 lf was actually 3 rolls of 10,000 lf, create 3 Rolls)
- Roll barcodes printed on receipt (use existing FedEx/ZPL printing pattern from your work codebase — same idea, different label content)

### Roll search (`/rolls`)
- Search by barcode (scan or type)
- Filter by stock, location, status, supplier lot
- Show: barcode, stock, lot, width, remaining, location, status

### Roll detail (`/rolls/{id}`)
- Full record including movement history
- Actions: Stage (move to a job's staging area), Split, Scrap, Return to Supplier

### Roll split
- Form: original roll, two (or more) child widths
- Sum of child widths must equal original width
- Original roll moves to Depleted, two new Rolls created with the same SupplierLotNumber, current remaining length, half the OriginalLength each (or whatever distribution makes sense)
- All three RollMovements logged

### Consumption from a job
- Operator screen (task 006) gains a "Scan Roll" step before starting a press operation
- Roll scanned → linked to JobOperation
- On clock-off, operator enters total Lf consumed → RollMovement appends, Roll.RemainingLengthLf decrements
- If consumption ≥ RemainingLengthLf, Roll status moves to Depleted

## Acceptance criteria
- [ ] All entities, configurations, migration in place
- [ ] PO entry → receipt → Roll creation flow works end-to-end
- [ ] Barcode uniqueness enforced at DB level (unique index)
- [ ] Roll search by barcode returns in <100ms for 100k+ rolls (index correctly)
- [ ] Roll split correctly creates child rolls preserving lot number
- [ ] RollMovement is append-only (domain rule, also enforced via no UPDATE/DELETE in service layer)
- [ ] JobMaterialUsage backfill: existing usage records get null RollId; new records require RollId
- [ ] Domain test: cannot consume more than RemainingLengthLf from a Roll
- [ ] Domain test: split totals must equal original width
- [ ] Integration test: full PO → receipt → consume on job → close out lifecycle
- [ ] Reconciliation report: for any stock, the sum of all open Rolls' RemainingLengthLf + sum of all Consumed = sum of all Received (within rounding tolerance)

## Out of scope
- WMS-level location tracking (shelf/bin granularity) — Tier 2; free-text location is enough
- ASN / EDI 856 inbound from suppliers (Tier 3)
- Cycle counting workflows (Tier 2; manual adjustments via roll edit is enough)
- Cost layer (FIFO/LIFO/avg) for finished goods — Tier 2
- Auto-PO suggestions from MRP (Tier 2)
- Roll holds for QC inspection (Tier 2)

## Deliverables
- PR with all entities, pages, integration into operator screen
- Branch: `agent/007-inventory`

## Notes
- This is the highest-leverage data integrity work in the whole MIS. If roll tracking breaks, traceability breaks, and traceability is why FDA/pharma customers pick a printer. Be conservative — prefer too many integrity checks over too few.
- Roll barcode format: human-readable + barcoded. Example: `R-2026-00042-A` for "Roll, year 2026, sequence 42, sub-letter A in case of splits". Print as Code128.
- Test the integrity report (the last acceptance criterion) as a unit test against seeded data, not just manually.
