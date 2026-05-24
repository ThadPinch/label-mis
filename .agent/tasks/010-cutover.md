# Task 010: Polish and Cutover (Phase 9)

## Goal
Replace whatever the shop uses today. Six users on the new system, parallel run for two weeks, then cutover.

## Context
- Read first: `AGENTS.md`
- Read first: `docs/tier1-buildout.md` Phase 9
- Prerequisite: Tasks 001-009 merged and in use internally for at least one week

## Scope

### Data migration
- [ ] Import existing customers (CSV from current system or spreadsheet)
- [ ] Import existing products (the recurring SKUs the shop runs regularly)
- [ ] Import existing open sales orders (so cutover doesn't lose work in flight)
- [ ] Import existing stock catalog (substrate library)
- [ ] Import existing dies (customer-owned tooling inventory)
- [ ] Import open AR balances as one-time opening balance invoices

Build importers as CLI tools in a new project `LabelsMis.Tools/Importers/` — not web pages. CLI tools are testable, repeatable, and don't pollute the UI. Each importer:
- Takes a CSV path argument
- Validates row-by-row, reports errors to console + log file
- Idempotent: re-running with the same file is safe (uses external key like customer code, sku, etc.)
- Wrapped in transactions; bad row rolls back the file by default

### User training
- [ ] 30-minute role-specific walkthrough per role (6 sessions total)
- [ ] Quick-reference card (one page, PDF) per role pinned at each workstation
- [ ] Recorded screencast for each role, hosted internally

### Parallel run period (2 weeks)
- [ ] Every order entered in both old and new systems
- [ ] Daily comparison report: any discrepancies investigated and resolved
- [ ] Bug log with severity rating (blocker / major / minor / cosmetic)
- [ ] Blockers fixed within 24 hours; majors within 3 days

### Cutover checklist
- [ ] All Tier 1 acceptance criteria from prior tasks confirmed in production-like conditions
- [ ] Backup strategy in place (Postgres pg_dump nightly, retained 30 days)
- [ ] Monitoring: at minimum, uptime check + error log aggregation
- [ ] Disaster recovery runbook (1 page) — what happens if the server dies tomorrow morning at 8am
- [ ] Final data migration: re-run all importers with fresh exports the morning of cutover
- [ ] Old system retained in read-only mode for 90 days post-cutover

### Documentation handoff
- [ ] User manual (per-role): 5-10 page PDF for each role, screen-by-screen
- [ ] Admin manual: how to add users, change roles, reset passwords, run backups, view logs
- [ ] System architecture diagram: one Mermaid diagram in `docs/architecture.md` showing the stack
- [ ] Schema ERD: `docs/schema-erd.md` with Mermaid ER diagram of all tables

## Acceptance criteria
- [ ] All six users have logged in and completed at least one real workflow without help
- [ ] Two weeks of parallel run with discrepancy rate <2%
- [ ] No blocker bugs open
- [ ] All Tier 1 features in active daily use
- [ ] Old system in read-only state, new system is the system of record
- [ ] Backups verified by a test restore

## Out of scope
- Anything in Tier 2 or Tier 3
- User analytics / usage tracking (Tier 2)
- A/B testing of UI variants (never)
- Multi-shop deployment (Tier 3)

## Deliverables
- Importer CLI tools in `LabelsMis.Tools/`
- Documentation in `docs/`
- Operational runbooks
- Branch: `agent/010-cutover`

## Notes
- Cutover is a people problem, not a code problem. By this phase the code is largely done; what's left is training, patience, and the daily grind of finding the gaps between what the system does and what the shop actually does.
- Resist the urge to add features during cutover. Anything that comes up that isn't a bug goes into `.agent/tasks/tier2/` as a future task. Tier 2 starts after cutover stabilizes, not during.
- Two-week parallel run is non-negotiable. If a customer calls and the new system can't answer, the shop falls back to the old system and trust erodes. The buffer is insurance.
