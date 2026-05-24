# Task 006: Jobs and Production (Phase 5)

## Goal
Sales orders become Jobs that follow the work through the shop. Operators clock on/off, count good/waste, capture downtime. End-of-job shows actual vs estimated cost.

## Context
- Read first: `AGENTS.md`
- Read first: `docs/tier1-buildout.md` Phase 5
- Read first: `docs/domain-reference.md` section 4 (production workflow)
- Prerequisite: Tasks 001-005 merged

## Entities to add

### Job
- Id, TenantId, JobNumber (`JOB-2026-00042`), SalesOrderLineId, ProductId, QuantityOrdered, QuantityPlanned (with overrun), Status (Planned/Prepress/Scheduled/OnPress/Finishing/Qc/Packed/Shipped/Closed enum), ScheduledForDate, DueDate, Priority (int, lower = higher priority), Notes, audit columns

### JobOperation
- JobId, Sequence (1, 2, 3...), OperationType (Press/Finishing/Inspection/Pack/Ship enum), EquipmentId (Press or FinishingOperation reference, polymorphic — use EquipmentType discriminator), PlannedStartAt, PlannedMinutes, ActualStartAt, ActualEndAt, Status (Pending/InProgress/Complete/Skipped enum), OperatorId, GoodCount, WasteCount, DowntimeMinutes, DowntimeReasonCode

### JobTimeEntry
- JobOperationId, UserId, ClockedInAt, ClockedOutAt
- One operator may clock in and out multiple times per operation (interrupted work)

### JobMaterialUsage
- JobId, RollId (nullable until task 007 — Roll inventory comes next phase, in v1 just track Stock + Length consumed manually)
- For Phase 5: JobId, StockId, QuantityUsedLf, UsedAt, UsedById, Notes
- Will be augmented in task 007 with RollId

## UI

### Sales order → Job
- On sales order detail page, "Schedule for Production" button
- Generates one Job per SalesOrderLine
- Generates JobOperations based on the Product's spec (Press first, then each FinishingOperation in order, then Pack, Ship)
- Status = Planned, awaiting scheduling

### Job list (`/jobs`)
- Filter by status, press, due date, customer
- Sort by due date, priority, status
- Quick actions: open job ticket print view (PDF), assign to schedule

### Job detail (`/jobs/{id}`)
- All planned vs actual operation data
- Cost summary: estimated cost from estimate vs actual cost from time entries + material usage
- Material usage list (what was consumed)
- Notes / audit trail

### Job ticket print view (`/jobs/{id}/ticket.pdf`)
- One-page PDF that follows the physical work
- Includes: job number (with barcode128), customer, product description, dimensions, substrate, ink set, quantity, due date, route (list of operations), notes
- Operator scans the barcode at their station to pull up the operator screen

### Operator screen (`/operator/job/{jobNumber}`)
- Tablet-friendly (large touch targets, minimal text)
- Shows job header, current operation
- Big "Clock On" button → creates JobTimeEntry
- During work: "Clock Off" + count entry (good, waste) + downtime entry (minutes + reason)
- When operation complete, advance to next operation
- Filter so operator only sees jobs assigned to their assigned equipment

### Simple scheduling (no gantt yet)
- On job detail page: "Schedule" form with date + equipment dropdown
- Job list filter by scheduled date shows the day's plan
- This is the manual stopgap until Tier 2 builds a real gantt

## Acceptance criteria
- [ ] All entities, configurations, migration in place
- [ ] Sales order → Job flow works for all sales order lines
- [ ] JobOperation generation correctly mirrors Product spec
- [ ] Job ticket PDF prints correctly with scannable barcode
- [ ] Operator screen works on a tablet at the press (test on actual tablet form factor in browser dev tools at minimum)
- [ ] Clock on/off creates time entries correctly; clock-off without clock-on errors gracefully
- [ ] Counts persist and roll up to job-level totals
- [ ] Domain test: job cannot be marked Closed if any operation is still InProgress
- [ ] Domain test: actual cost calculation = sum(time_entries × press/equipment cost-per-hour) + material cost
- [ ] Integration test: full flow order → job → operations → clock cycles → close

## Out of scope
- Real-time updates / WebSockets (refresh-based is fine; Tier 2)
- Gantt scheduling UI (Tier 2)
- HP PrintOS or any DMI integration (Tier 2)
- QC module beyond a notes field on the operation (Tier 2)
- Job-level photos / attachments (Tier 2)
- Multi-operator concurrent work on the same operation (Tier 2)
- Re-runs / re-makes (treat as a new job)

## Deliverables
- PR with all entities, pages, PDF, operator screen, and tests
- Branch: `agent/006-jobs`

## Notes
- The operator screen is the highest-stakes UI in the system. If operators won't use it, the whole MIS bypasses production data and you lose the cost-tracking value. Keep it dead simple. No clever menus. Big buttons. Sans-serif. High contrast.
- Downtime reason codes: seed a small list (Mechanical, Material, Operator, Setup, AwaitingWork, Other) as an enum. Customizing reason codes is Tier 2.
- The polymorphic EquipmentId on JobOperation is a known design smell. Two cleaner alternatives: (a) separate PressJobOperation and FinishingJobOperation tables, or (b) a polymorphic Equipment supertype. Pick one and document it. Don't paper over it with reflection.
