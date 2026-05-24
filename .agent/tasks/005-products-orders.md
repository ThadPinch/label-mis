# Task 005: Products and Sales Orders (Phase 4)

## Goal
Won estimates become Products (reusable specs). Customer POs become Sales Orders referencing those Products. Repeat orders skip the estimating step.

## Context
- Read first: `AGENTS.md`
- Read first: `docs/tier1-buildout.md` Phase 4
- Read first: `docs/domain-reference.md` section 2 (Products entity)
- Prerequisite: Tasks 001-004 merged

## Entities to add

### Product
- Id, TenantId, CustomerId, CustomerSku (customer's part number, optional), InternalSku (auto-generated: `<CustomerCode>-<seq>`), Description, SourceEstimateId, LabelAcrossIn, LabelAroundIn, CornerRadiusIn, SubstrateId, InkSet, FinishingOperationsJson, DieId (nullable — may not have a die yet for first run), ArtworkFilePath (nullable), Status (Active/Discontinued enum), audit columns, IsActive

### RollSpec
- ProductId (one-to-one), LabelsPerRoll, CoreSizeIn (1/3/6), UnwindPosition (int 1-8), MaxOdIn (max outer diameter), RollsPerCase, CaseLabelFormat (nullable text)

### SalesOrder
- Id, TenantId, OrderNumber (`SO-2026-00042`), CustomerId, CustomerPoNumber, OrderedAt, RequestedShipDate, Status (Open/InProduction/Shipped/Invoiced/Closed enum), Notes, audit columns

### SalesOrderLine
- SalesOrderId, LineNumber, ProductId, Quantity, UnitPrice (from product's most-recent estimate at this qty, overrideable), LineTotal, LineNotes

## UI

### Won estimate → Product
- On the estimate detail page when status = Won, show "Create Product" button (only if no product yet exists from this estimate)
- Clicking generates a Product, copies all spec fields from estimate, leaves RollSpec and Die blank for the user to fill in
- Redirect to product edit page

### Product pages (`/products`)
- List page: filter by customer, status, search by SKU or description
- Edit page: all product fields, including roll spec, die assignment (typeahead from dies linked to this customer or shop-owned), artwork file upload (just stored as a path for now; S3 in Tier 2)
- New page: same form but starting blank (for products not derived from an estimate — rare but supported)

### Sales order pages (`/sales-orders`)
- List page: filter by status, customer, ship date
- Order entry page (`/sales-orders/new`):
  - Customer selector
  - PO number (free text — the customer's PO number, not ours)
  - Requested ship date
  - Line items: pick product from typeahead (filtered to that customer's products), enter quantity, unit price defaults from product's last estimate at that quantity tier but is editable
  - Save → status = Open
- Edit page: same as new while status = Open; locked once status moves past Open

## Acceptance criteria
- [ ] All entities, configurations, migration in place
- [ ] Won estimate produces a product cleanly
- [ ] InternalSku generation is deterministic and unique per customer (use a Postgres sequence per customer, or `<CustomerCode>-<count+1>`)
- [ ] CRUD pages work end-to-end
- [ ] Order line unit price defaults from the estimate's matching quantity break, but is editable with a warning if changed
- [ ] Domain test: a sales order cannot be Edited once Status moves past Open
- [ ] Domain test: a product cannot be Discontinued if there's an Open or InProduction sales order line referencing it
- [ ] Integration test: full flow estimate → won → product → order persists correctly
- [ ] Permissions: Estimator can create products, CSR can create sales orders, both can view; Admin can override discontinued/locked state

## Out of scope
- Sales order acknowledgment PDF (Tier 2)
- EDI 850 inbound (Tier 3)
- Customer self-service ordering (Tier 2)
- Backorder management (split lines into multiple shipments) — keep it simple, one shipment per line in Tier 1
- Product changes triggering price updates across open orders (manual handling for now)

## Deliverables
- PR for products
- PR for sales orders (can be same PR if scope stays tight)
- Branch: `agent/005-products-orders`

## Notes
- The Product entity is the most-edited entity in the system long-term. Spec drift (customer changes specs over time) needs to be handled in Tier 2 with versioning. For Tier 1, just allow edits and warn the user that this will affect future orders.
- ArtworkFilePath is just a string in Tier 1. Don't build file management. The shop will paste in network paths or upload to a local folder and reference it. S3 + preview in Tier 2.
