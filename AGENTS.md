# AGENTS.md

Project conventions for any AI agent (or human) working on this codebase. Read this file before starting any task.

---

## Project: Labels MIS

A Management Information System for a narrow-web label printing shop. Single HP Indigo 6800 digital press, 6 users, off-line finishing. Replaces spreadsheets + sticky notes + QuickBooks-only workflow.

**Current phase**: Tier 1 (MVP). See `docs/tier1-buildout.md` for the phase plan and `docs/domain-reference.md` for industry terminology.

---

## Stack

- **.NET 10** (preview SDK pinned in `global.json`)
- **C# 13**
- **ASP.NET Core** with **Razor Pages** for UI
- **Entity Framework Core 10** code-first
- **PostgreSQL 16** as the database (via Npgsql)
- **ASP.NET Identity** for auth
- **QuestPDF** for PDF generation (quotes, invoices, job tickets)
- **xUnit** + **FluentAssertions** for tests
- **Docker Compose** for local Postgres
- **GitHub Actions** for CI

Do not introduce new top-level dependencies without updating this file in the same PR.

---

## Project structure

```
src/
├── LabelsMis.Domain/         entities, value objects, business rules
│                             NO references to Infrastructure or Web
├── LabelsMis.Infrastructure/ EF DbContext, migrations, external clients
│                             NO references to Web
└── LabelsMis.Web/            Razor Pages, controllers, views, Program.cs

tests/
├── LabelsMis.Domain.Tests/
├── LabelsMis.Infrastructure.Tests/
└── LabelsMis.Web.Tests/
```

**Dependency rule**: Web → Infrastructure → Domain. Never the reverse. The Domain project knows nothing about EF, HTTP, or any external system.

---

## Database rules

**Always check before creating migrations.** This is the most important rule in this document.

Before running `dotnet ef migrations add`:
1. Read every existing migration file in `src/LabelsMis.Infrastructure/Migrations/`
2. Run `dotnet ef migrations list` to see what's already applied
3. Confirm the change you're about to make hasn't already been made by an earlier migration in a different shape
4. After generating, open the new migration file and read both `Up` and `Down` methods — never trust the EF scaffold blindly

Before any schema change:
- Update the entity in `LabelsMis.Domain/Entities/`
- Update the EF config in `LabelsMis.Infrastructure/Persistence/Configurations/`
- Run `dotnet ef migrations add <DescriptiveName>` (PascalCase, no spaces)
- Verify the generated migration matches intent
- Test the `Down` migration locally by applying then reverting
- Commit the entity change AND the migration in the same commit

**Never edit a migration after it's been merged to main.** Add a new one to correct it.

Naming: migrations are named `<Verb><Object>` — e.g. `AddCustomerSoftDelete`, `RenameStockToSubstrate`, `IndexJobsByStatus`. No timestamps in the name (EF adds them automatically).

---

## Schema conventions

Every business entity table has:
- `Id` (Guid, primary key, NOT auto-increment)
- `TenantId` (Guid, NOT NULL, default Guid value seeded as 'default-tenant') — multi-tenant shape from day one even though v1 is single-tenant
- `CreatedAt` (timestamptz, NOT NULL, default now())
- `CreatedById` (Guid, FK to AspNetUsers)
- `ModifiedAt` (timestamptz, nullable)
- `ModifiedById` (Guid, FK to AspNetUsers, nullable)

Master data tables (Customer, Product, Stock, Die, Supplier) also have:
- `IsActive` (bool, NOT NULL, default true) — soft delete; never DELETE rows

Transactional tables (Estimate, SalesOrder, Job, Invoice, Shipment) do NOT have IsActive — they have a status enum and are immutable in non-draft states.

Money is `decimal(18,4)`. Dimensions are `decimal(10,4)`. Quantities are `int` for counts, `decimal(14,4)` for measured quantities (linear feet, MSI). Never use `float` or `double` for business numbers.

All `Id` columns are `Guid` generated client-side (so the Domain can create entities without round-tripping to the DB for an ID). EF should not generate identity values.

Foreign keys always end in `Id`. Navigation properties never end in `Id`. Example: `CustomerId` (FK column) and `Customer` (nav property).

---

## Code conventions

### Domain layer
- Entities are classes with private setters and a private parameterless constructor (for EF)
- Domain methods enforce invariants; do not allow construction of invalid state
- Value objects are `record` types
- No data annotations on entities; all config lives in `IEntityTypeConfiguration<T>` classes in Infrastructure

### Infrastructure layer
- One `IEntityTypeConfiguration<T>` per entity, in `Persistence/Configurations/`
- DbContext is `LabelsMisDbContext`, registered as scoped
- External service clients live in subfolders: `Fedex/`, `QuickBooks/`, `Email/`
- Each external client has an interface in Domain (`IFedexClient`) and implementation in Infrastructure (`FedexClient`)

### Web layer
- Razor Pages, not MVC controllers, unless there's a specific reason
- Page models inject services, not the DbContext directly
- Service layer in `LabelsMis.Web/Services/` orchestrates Domain + Infrastructure
- ViewModels live next to the page that uses them
- No business logic in page models — they delegate to services or domain entities

### Naming
- C# types: PascalCase (`CustomerService`, `EstimateLineItem`)
- Local variables: camelCase (`estimateTotal`, `quantityBreaks`)
- Private fields: `_camelCase` with underscore prefix
- Constants: PascalCase (`MaxLabelsPerImpression`)
- Database tables: PascalCase singular matching the entity (`Customer`, not `customers`)
- Database columns: PascalCase matching the property
- Razor page routes: kebab-case URLs (`/sales-orders/edit/{id}`)

### Async
- All I/O is async
- Method names end in `Async` for async methods
- Pass `CancellationToken` through every async chain that goes to the DB or out to the network

---

## Testing

### What to test
- **Domain logic**: every public method on an entity, every calculation in a service. Aim for 90%+ coverage in Domain.
- **Service orchestration**: the happy path of every service in Web/Services
- **Infrastructure**: only test things that depend on EF behavior or external service contracts; do not test EF itself

### How to test
- xUnit, FluentAssertions
- Each test class mirrors the SUT class name: `EstimatingServiceTests` for `EstimatingService`
- Use `[Fact]` for single scenarios, `[Theory]` with `[InlineData]` for parameterized
- Test names: `MethodName_Scenario_ExpectedOutcome` — e.g. `Calculate_WhenLabelTooWide_ReturnsError`
- One Assert per test where possible; use `.Should().Satisfy(...)` for grouped assertions

### CI gate
PRs must have green CI before merge. CI runs:
- `dotnet build` with warnings as errors
- `dotnet test` — all tests must pass
- `dotnet ef migrations script --idempotent` — must produce a valid SQL script (catches broken migrations)

---

## Git workflow

- Branch per task: `agent/<task-number>-<slug>` or `feature/<slug>`
- One PR per task
- Commit messages: imperative present tense, ≤72 char subject, optional body
  - Good: `Add Customer entity and migration`
  - Good: `Fix imposition rounding for narrow labels`
  - Bad: `customer stuff`
  - Bad: `Added the customer entity`
- Squash-merge to main (one commit per PR in history)
- PR description follows `.github/pull_request_template.md`

---

## Working with the codebase as an agent

### Before starting a task
1. Read this file (AGENTS.md)
2. Read the task file in `.agent/tasks/`
3. Read every doc listed in the task's "Context" section
4. Run `dotnet build && dotnet test` to confirm the codebase is green before changes
5. Create a branch: `agent/<NNN>-<slug>`

### During a task
- Make changes in small, logical commits
- Run `dotnet test` after every meaningful change
- If you need to add a migration, see "Database rules" above — always check first
- If a decision diverges from the task spec, update the spec in the same PR
- If you find unrelated bugs, do NOT fix them in this PR — open a new task file

### Before finishing a task
- All acceptance criteria in the task file are met
- All tests pass
- No new compiler warnings
- No commented-out code, no `TODO` comments without an associated task file
- Updated `AGENTS.md` if you introduced a new convention
- PR description fills out the template

### What NOT to do as an agent
- Do not modify migrations that already exist on main
- Do not add nuget packages without justification in the PR
- Do not change project structure or solution layout without an explicit task
- Do not add files outside the locations described in this document
- Do not silently change established business rules
- Do not use `dynamic`, `object`, or reflection unless there's no alternative
- Do not introduce a new architectural pattern (MediatR, CQRS, etc.) — keep it boring

---

## Documentation conventions

`docs/` contains reference material that outlives any single task:
- `domain-reference.md` — industry concepts (Cerm/LT/Radius synthesis)
- `tier1-buildout.md` — phase-by-phase build plan
- `estimating-engine.md` — full spec for the calculation engine
- `schema-erd.md` — entity relationship diagram (Mermaid or PNG)
- One file per major subsystem as they get built

`.agent/tasks/` contains work orders for the agent:
- `NNN-slug.md` numbered sequentially
- Short, pointer-style — the spec lives in `docs/`, the task file references it
- Mark completed tasks by moving them to `.agent/tasks/done/` after merge

---

## Conventions for this specific domain

Industry-specific things that will trip up a generic agent:

- **MSI** = Thousand Square Inches, the standard pricing unit for label stock in North America. NOT square meters. NOT million square inches.
- **fpm** = feet per minute (press / finisher speed). Already encodes "per minute"; do NOT multiply by 60.
- **Impression** on an HP Indigo = one printed image cycle on the press. Click charges are per impression.
- **Repeat length** ≠ label height. It's the cylinder circumference for flexo, or the imposed sheet length for digital.
- **Roll** is an inventory unit. A roll has a barcode and a lot number and gets consumed linearly. When you cut a 13" roll into two 6.5" rolls, you create two new rolls and consume the original.
- **Customer-owned tooling** (dies, plates) — common in this industry. The shop stores them but the customer paid for them. Tracked separately from shop-owned consumables.
- **EPM** = Enhanced Productivity Mode on HP Indigo: 3 inks instead of 4, lower click rate, slight color compromise.

When code involves these concepts, prefer variable names that match industry terminology even if they're shorter than ideal C# names. `webWidthIn`, not `pressLateralCapacityInInches`.

---

## Open questions / decisions deferred

These are decisions the project has explicitly deferred. If you encounter a task that needs an answer, ask the human before guessing:

- Internal accounting vs QuickBooks export-only — current answer: export-only for Tier 1
- Hosting target (EC2 Windows / Linux container / managed service) — current answer: undecided, build deployment-agnostic
- Backup/disaster recovery strategy — current answer: out of scope for Tier 1
- Customer portal — current answer: Tier 2, not Tier 1
- Mobile app — current answer: responsive web is enough for Tier 1
