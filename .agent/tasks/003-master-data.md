# Task 003: Master Data (Phase 1)

## Goal
Build CRUD for all the reference data that the estimating engine and downstream phases need. After this task, an estimator can sit down and enter a complete picture of a new customer with their substrate and tooling before any estimate exists.

## Context
- Read first: `AGENTS.md` (schema conventions, naming, audit columns, soft delete rules)
- Read first: `docs/tier1-buildout.md` Phase 1
- Read first: `docs/domain-reference.md` sections 2, 15 (entities and industry concepts)
- Prerequisite: Tasks 001 (scaffold) and 002 (estimating engine) merged

## Entities to build

In order — each is its own sub-task, but the same PR is fine if scope stays tight. If the PR gets larger than ~40 files, split it.

### 1. Customer + Address + Contact
- `Customer`: Name, Code, Terms, TaxExempt, DefaultMarkupPct, Status, SalesRepId, IsActive
- `Address`: CustomerId, AddressType (Billing/Shipping), Street1, Street2, City, State, Zip, Country, IsDefault
- `Contact`: CustomerId, FirstName, LastName, Email, Phone, Role, IsPrimary

### 2. Supplier + SupplierContact
- `Supplier`: Name, Code, Terms, DefaultLeadTimeDays, AccountNumber, IsActive
- `SupplierContact`: same shape as Customer Contact

### 3. Stock (substrate)
- `Stock`: Code, Description, FaceMaterial, Adhesive, Liner, TotalCaliperMil, WidthIn, SupplierId, SupplierPartNumber, CostPerMsi, MinOrderQtyLf, IsActive
- `StockCostHistory`: StockId, CostPerMsi, EffectiveDate, RecordedById

### 4. Die
- `Die`: Description, CustomerId (nullable), DieType (Flexible/Solid/Sheeter), Shape, LabelAcrossIn, LabelAroundIn, CornerRadiusIn, GutterAcrossIn, GutterAroundIn, LabelsAcross, LabelsAround, RepeatLengthIn, WebWidthIn, SupplierId, SupplierPartNumber, Location, LastUsedAt, UsageCount, RetiredAt, IsActive
- `DieUsage`: DieId, JobId (nullable since Jobs come later — for now allow null), UsedAt, UsedById, Notes

### 5. Ink
- `Ink`: Code, Description, InkSet (CMYK/CMYKW/CMYKW_PlusSpot/EPM enum), ClickRatePer1000, IsWhite, IsActive

### 6. FinishingOperation
- `FinishingOperation`: Code, Description, OperationType (Laminate/Varnish/DieCut/Slit/Sheet/Foil/Emboss/Perf enum), DefaultSetupMinutes, DefaultRunSpeedFpm, EquipmentName, CostPerHour, IsActive

### 7. Press
- `Press`: Name, Code, PressType (DigitalToner/DigitalInkjet/Flexo/Hybrid enum), WebWidthIn, MaxRepeatIn, MinRepeatIn, MaxColors, SpeedFpm, SetupMinutes, CostPerHour, IsClickBased, IsActive
- Seed one row: Indigo 6800 with realistic defaults

## Acceptance criteria

For each entity:
- [ ] Domain class in `LabelsMis.Domain/Entities/`
- [ ] `IEntityTypeConfiguration<T>` in `LabelsMis.Infrastructure/Persistence/Configurations/`
- [ ] `DbSet<T>` added to `LabelsMisDbContext`
- [ ] One migration covering all entities in this task (per `AGENTS.md` "Database rules" — check first!)
- [ ] Razor Pages CRUD under `/Pages/<EntityPluralName>/`:
  - List page with search, sort, pagination
  - Create page
  - Edit page
  - Delete = soft delete (set IsActive = false), no hard delete
- [ ] Permissions enforced via Identity roles (Admin + Estimator can edit master data; CSR can read-only)
- [ ] Server-side validation with clear error messages
- [ ] Domain unit tests for any business rules (e.g. Die's `LabelsAcross` must equal `floor((WebWidthIn + gutter) / (LabelAcrossIn + gutter))` when set programmatically)
- [ ] Integration test confirms each entity persists and reads back correctly through EF

## Out of scope
- Product (Phase 4 entity — comes after sales orders)
- Roll inventory (Phase 6)
- Cost history beyond Stock — keep it simple
- Bulk import / CSV upload (later task if needed)
- Audit log table beyond CreatedBy/ModifiedBy columns
- Multi-tenant filtering UI (column exists per AGENTS.md but no per-tenant switching needed in v1)

## Deliverables
- One PR per logical group if needed (Customer+Address+Contact as one PR, Supplier as another, etc.)
- Each PR independently passes CI
- Branch naming: `agent/003a-customers`, `agent/003b-suppliers`, etc.

## Notes
- These pages will be edited many times in future phases. Keep them simple. No fancy widgets, no SPA bits. Plain Razor with Bootstrap form controls is fine.
- Resist the urge to add features not listed. "Wouldn't it be nice if we also had..." goes in a new task file.
