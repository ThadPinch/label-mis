# Task 008: Shipping (Phase 7)

## Goal
Pack a job, generate a FedEx label, capture tracking, notify customer. The shipping module is the customer-visible polish that separates a real MIS from a spreadsheet.

## Context
- Read first: `AGENTS.md`
- Read first: `docs/tier1-buildout.md` Phase 7
- Read first: `docs/domain-reference.md` section 12 (shipping)
- Prerequisite: Tasks 001-007 merged

## Entities to add

### Shipment
- Id, TenantId, ShipmentNumber (`SHP-2026-00042`), SalesOrderId, ShipDate, Carrier (enum: Fedex; UPS/DHL/LTL deferred to Tier 2), ServiceLevel (FedexGround/FedexExpressSaver/Fedex2Day/FedexOvernight enum), ShipFromAddressId, ShipToAddressId, Status (Pending/InTransit/Delivered/Exception/Returned enum), TotalDeclaredValue, BillingType (Sender/Recipient/ThirdParty enum), BillingAccountNumber (when not Sender), audit columns

### ShipmentPackage
- ShipmentId, PackageNumber (1, 2, 3...), WeightLb, LengthIn, WidthIn, HeightIn, TrackingNumber (unique, indexed), LabelUrl (path to PDF/PNG label saved locally), DeclaredValue

### ShipmentLine
- ShipmentId, SalesOrderLineId, JobId (links back to which job produced this), QuantityShipped
- Multiple ShipmentLines per Shipment (multi-line orders ship as one shipment when possible)
- Multiple Shipments per SalesOrderLine allowed (partial shipping)

### TrackingEvent
- ShipmentPackageId, EventAt, StatusDescription, Location, RawPayload (JSONB)
- Append-only

## FedEx integration

Reuse patterns from your work codebase but the integration code lives in `LabelsMis.Infrastructure/Fedex/`:

- `IFedexClient` interface in Domain
- `FedexClient` implementation in Infrastructure
- Auth: OAuth2 token-based, credentials in `appsettings.json` (with environment-variable override for production)
- Methods needed:
  - `GetRateAsync(rateRequest)` → returns rate options across service levels
  - `CreateShipmentAsync(shipmentRequest)` → returns tracking number + label
  - `CancelShipmentAsync(trackingNumber)`
  - `GetTrackingAsync(trackingNumber)` → returns events

### Label printing
- Generate as PDF for download/print
- ZPL output supported for thermal label printers (QZ Tray pattern from your work code is a good reference but keep the actual integration loose-coupled)
- Settings page for label printer config

## UI

### Pack & ship (`/shipments/new?salesOrderId={id}`)
- Open sales order, click "Ship This Order"
- Form pre-fills ship-to address from customer's default shipping address
- Lines auto-populate with sales order lines and their job quantities (with override for partial shipping)
- Add packages: enter weight + dimensions per package (typical workflow: weigh box on scale, type number)
- Rate quote: live call to FedEx, shows costs across service levels
- Pick service level
- Generate label → FedEx call → save tracking number + label PDF → status moves
- Print label (PDF download or ZPL print)
- Optionally email customer with tracking number

### Shipment list (`/shipments`)
- Filter by status, date range, customer, carrier
- Sort by ship date
- Quick view of tracking status (pulled from latest TrackingEvent)

### Tracking updates
- Background job (Quartz.NET or BackgroundService) polls FedEx every 15 minutes for shipments with status InTransit or Pending
- New events appended to TrackingEvent table
- Shipment.Status updated when carrier reports Delivered
- On Delivered, send customer email "Your shipment has arrived"

## Acceptance criteria
- [ ] All entities, configurations, migration in place
- [ ] FedEx integration works against the FedEx sandbox (use sandbox API in tests; production credentials in prod config only)
- [ ] Rate quote returns within 3 seconds (cache nothing — rates change)
- [ ] Label generation produces a scannable label (test by scanning a generated label with phone barcode reader)
- [ ] Tracking polling works (mock the FedEx tracking API in tests)
- [ ] Customer email on ship goes out reliably
- [ ] Partial shipping: ship 2 of 3 ordered → sales order status remains In Production, not Shipped
- [ ] Full shipping: ship all lines fully → sales order status moves to Shipped
- [ ] Domain test: Shipment cannot be created with zero packages
- [ ] Domain test: total shipped quantity per line cannot exceed ordered quantity
- [ ] Integration test: sandbox FedEx call returns a label (skip in normal CI; run on-demand)

## Out of scope
- UPS, DHL, LTL freight (Tier 2 — design FedexClient with an interface so swapping is straightforward)
- International / customs documentation (Tier 2)
- BOL generation for freight (Tier 2)
- Return labels (Tier 2)
- ASN / EDI 856 outbound (Tier 3)
- Multi-piece freight (palletization) — Tier 2
- Rate shopping across carriers (Tier 2)
- Address validation against USPS / FedEx address API (Tier 2 — for now trust the entered address)

## Deliverables
- PR with all entities, pages, FedEx integration, background polling
- Branch: `agent/008-shipping`

## Notes
- FedEx sandbox credentials must NEVER be committed. Use `dotnet user-secrets` for local dev, env vars for prod.
- The tracking poller should respect a back-off if FedEx returns 429 or errors. Don't hammer their API.
- Customer email content: keep it short. Subject "Your order has shipped". Body: tracking number, link to FedEx tracking page, a "reply to ask questions" line. No marketing copy. The customer cares about one number, not the prose around it.
