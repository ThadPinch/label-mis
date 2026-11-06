# QuickBooks Phase 0 (Track E): design choices

Status: **E1–E12 built** on `feature/epm-qb-phase0`, merged to `main` and deployed. Every task has its
own commit with the migration proof and test/smoke evidence in the body. E7–E11 follow the designs
Thad approved on 2026-11-01; the E6 shipped-for-billing rule is his decision of the same day. Written
2026-11-01 against `origin/main` @ eaa4447.

Sources: Andrew's 2026-10-24 work-queue email (Track E + Appendix A) and
`plans/2026-11-01-epm-qb-phase0-plan.md` in the LabelShop project folder.

## Goal

Sabrina keys every invoice into QuickBooks Online (QBO) by hand. That takes about 20 steps, and 14
of them re-type data LabelShop already has. Phase 0 changes LabelShop only, with no QBO API, and
does three things:

- it removes the "unlock the SO, type the shipping, save" step;
- it removes the need to re-check every price;
- it adds one screen that already matches the QB invoice.

Phase 1 adds the API push. Nothing in Phase 0 should block it (see "Phase 1 compatibility").

## Shipping rules (E1–E4)

| Shipping method on the order | Billed shipping per shipment |
|---|---|
| Pickup | $0 |
| Customer account (Knight Transportation, shipped on the customer's own account) | $0 |
| Local delivery | Flat fee setting (default $25) |
| Carrier (FedEx and any other carrier type; **an order with no method is treated as a carrier shipment**) | Carrier cost × multiplier setting (default 1.5). Stays *pending* (null) until a carrier cost is entered |

Amounts are rounded to cents, half away from zero.

### E1: Shipping methods master data

- New `ShippingMethodType.CustomerAccount` (= 3) and a fixed-Guid method
  `22222222-2222-2222-2222-222222222204` named "Customer account". It is $0, requires an address,
  and has the note "Knight Transportation — bills to customer, $0". It follows the same pattern as
  Pickup, Delivery and FedEx (`…201`–`…203`).
- `ShippingMethod.Notes` (nullable, 500 characters) is edited on Setup → Shipping methods, listed
  there, and shown under the method dropdown on estimates and sales orders. FedEx gets the note
  "estimate — replaced by actual × markup at shipment".
- The method's `Price` stays the **quote**. FedEx's $35 is still what an estimate or SO quotes. The
  amount actually billed comes from the shipment (E3/E4).
- **Why a migration and not the seeder.** `MasterDataSeeder` returns early as soon as the WS6800
  press and any shipping method exist, so it never adds defaults to production. Instead,
  `AddShippingMethodNotes` does two things:
  - It sets the FedEx note only while that row's `Notes` is still null.
  - It inserts the Customer account row, copying `TenantId` and `CreatedById` from the oldest
    method row. The insert is skipped if that Id exists, if any method is named "customer account"
    (any case, surrounding spaces ignored), or if any method already has type 3.

  Fresh installs get the row from `ShippingMethod.CreateDefaults`.

### E2: Carrier cost on the shipment

- `Shipment.CarrierCost` is `numeric(18,4)`. Null means "not known yet".
- It is entered for the whole shipment on Record shipment. If that field is left blank, the
  per-package costs are used when any were typed. If none were typed, it stays null.
- The shipment page has a "Shipping charges" card for entering, correcting or clearing it later,
  because the WWEX bill often arrives after the shipment. Admin, Shipping and Accounting can edit
  it, and negative values are rejected.
- FedEx label flow: if no carrier cost was entered, the labels' rated total becomes the carrier
  cost. It can still be edited afterwards.

### E3: Billed shipping

- `Shipment.BilledShipping` (`numeric(18,4)`, nullable) is persisted. It is computed every time a
  shipment is created or shipped (Record shipment, one-click Mark shipped / picked up, the FedEx
  label flow) and recomputed whenever the carrier cost is edited.
- Settings live on the existing `GeneralSettings` row and are edited on Setup → General settings:
  - `LocalDeliveryFee`: money, default 25.
  - `CarrierCostMultiplier`: a multiplier, not a margin. Default 1.5.

  The migration adds both as NOT NULL columns with database defaults, so the existing settings row
  picks the defaults up. There is **no minimum charge** (see the open questions).
- The shipment page shows billed shipping read-only under the carrier cost, along with the rule
  used (for example "FedEx · Carrier cost × 1.5"). The Record form shows a live preview.
- Shipments recorded before this change have no stored value (null). They are not backfilled.
  Where an invoice needs them, their billed shipping is worked out on the spot from the order's
  method and their carrier cost, or package cost if there is no carrier cost.

### E4: The invoice's shipping comes from the shipments

A **draft** invoice's shipping is resolved as follows (`InvoiceShippingRules.Resolve`):

1. If a manual override is set (`Invoice.ShippingOverride`, Accounting), it wins.
2. If nothing has shipped yet, the shipping quoted on the order is used, as before.
3. Otherwise it is the sum of the shipped shipments' billed shipping.
4. While any shipped shipment is still *pending* (no carrier cost yet), the invoice keeps its
   current amount and shows "Pending carrier cost on SHP-…".

Sent, paid and QB-exported invoices are never rewritten.

The invoice is updated from the shipment side: after Record shipment, Mark shipped / picked up,
the FedEx label flow, and a carrier-cost edit. The SO is never unlocked. Creating an invoice from
an order or a shipment, and the existing SO-edit sync, use the same rule, so editing a price no
longer resets shipping to the quote.

### E5: Non-billable invoices

- An invoice now has `NonBillable` plus a required reason (500 characters), and who/when.
- It can be set on draft or sent invoices that are not exported, void or paid. It is refused once
  any payment exists. It can be undone with "Make billable again".
- Non-billable invoices:
  - leave the Ready tab;
  - are listed under the renamed **Exported / closed** tab with a badge and the reason;
  - are refused by `MarkExported`;
  - are skipped by both CSV exports;
  - are left out of AR aging.
- No QuickBooks record is created. Whether QB needs one is an open question below.

## E6: Ready vs Waiting

An unexported, billable invoice is **Ready** when all of these hold, otherwise it is listed on the
**Waiting** tab with its reasons (`InvoiceReadinessRules`):

- the order counts as shipped, **or** the invoice is flagged *Bill before shipping*;
- total > 0;
- nothing blocks it (carrier cost pending, customer blockers from E7).

**Shipped for billing** (Thad's decision): every line's shipped quantity ≥ its ordered quantity,
**or** the sales order's own status is Shipped / Invoiced / Closed. A partial shipment does not make
the invoice Ready: the order bills once, when it has all shipped, unless *Bill before shipping* is set.

**Under-runs.** Shipping 950 of 1,000 is normal, but it used to leave the order stuck: Record shipment
moves the job to Shipped, so the order drops off *Ready to ship*, one-click *Mark shipped* refuses it,
and the SO stays In production for ever. The shipment page now has a **Mark order shipped** card when
a line shipped short (`ShipmentService.MarkOrderShippedAsync`: no new shipment, SO → Shipped; refused
while any line has shipped nothing; Admin / Shipping / Accounting). The Waiting reason says how:
"Not shipped yet (0 of 1 lines). If it under-ran, open its shipment and use Mark order shipped".

Reasons listed on Waiting:

- "Not shipped yet (x of y lines)" (+ the under-run hint once something has shipped);
- "Nothing on the order ships — use Bill before shipping" (charge-only orders);
- "$0 total — close it as non-billable or add charges";
- "Waiting on carrier cost (SHP-…)" (the E4 pending rule);
- the E7 customer blockers.

Invoices page: **Ready to export / Waiting / Exported – closed** tabs with counts; checkboxes only on
Ready. Bulk *Mark exported*, *Export selected* (CSV) and the date-range CSV export act only on Ready
invoices (re-exporting already-exported ones is unchanged); the bulk message says how many were
skipped. The invoice page has a QuickBooks card (Ready / Waiting + reasons) with the *Bill before
shipping* toggle (Admin / Accounting). Migration `AddInvoiceBillBeforeShip` (bool, default false).

## E7: Customer QuickBooks fields and blockers

- `Customer.QuickBooksCustomerId` (text, 100), `TaxSetupComplete` + `TaxSetupCompletedAt` (date) on
  the customer form. Ticking without a date stamps today; unticking clears the date.
- Blockers: "Waiting on new-client form (tick Tax setup complete on the customer)" and "No billing
  contact with email (add a contact with role Billing on the customer)".
- ~~Contact roles are free text, so a billing contact is one whose role contains a billing word~~ —
  superseded 2026-11-05 by the **Billing** checkbox on contacts (see "After Phase 0" below). The
  role-word rule survives only in the migration that ticked the box on the contacts it matched.
- **Deploy-day backfill** (migration `AddCustomerQuickBooksSetup`): customers with at least one
  invoice already marked exported are set up, dated by their first export — they went through the
  new-client form long ago. Without it every existing invoice would jump to Waiting. Customers never
  exported stay gated. Only rows still false are touched, so a re-run changes nothing.

## E8: Setup → QuickBooks mapping

`/settings/quickbooks` (Admin and Accounting; linked from Finance and Setup → Administration). Stored
in a new single-row table `QuickBooksSettings`; until the first save the defaults live in code.

- One QB item per line type: Sales (product lines), Shipping, Design, Rush, Die Creation. Class names:
  Roll Label, Shrink Sleeves, Buyout. QB **names** only; Phase 1 adds ItemRef / ClassRef ids.
- Class rule (`QuickBooksMappingRules`): a product line on the buyout substrate code (default
  `OUTSOURCED`, case-insensitive) or an outsourced line → Buyout; a shrink stock → Shrink Sleeves;
  else Roll Label. The substrate is the line's spec stock, else the product's. Charge **and shipping**
  lines inherit the job's class = the order's **first** product line (Roll Label when there is none).
- **Charges have no type**, so the least invasive classifier is the description: an editable keyword
  list per type (defaults: die, tooling, plate / rush, expedite / design, art, artwork, proof). A
  keyword matches the start of any word (`die` matches "Die creation" and "diecut"), checked Die
  Creation → Rush → Design; no match → Sales, with a warning on the preview. No schema change on
  charges; the per-line override (E9/E10) fixes misfits.
- The page shows the E3 local delivery fee and carrier multiplier read-only, linked to General settings.
- `QuickBooksMappingService.MapInvoiceAsync` returns (item, class, and why) for every invoice line and
  the shipping line.

## E9: QuickBooks-shaped model and JSON

`QuickBooksInvoiceService.GetPreviewAsync`, also at `GET /invoices/{id}/quickbooks.json` (same
`InvoicesRead` policy as the invoice pages; 404 for an unknown id). It holds DocNumber (= INV #),
status (Ready / Waiting / Exported / Non-billable / Void), invoice date, terms (LabelShop's) and due
date, customer name + QB id, PO, order, billing email, ship-to (the order's, else the customer's
default shipping, else billing address) and bill-to, the lines (item, class and why, description,
**quantity ordered**, rate, amount), the shipping line, lines total and total **before tax**,
LabelShop's subtotal / tax / total for reference, blockers, warnings, exported at / by, and the
dropdown options.

- **Amount = what LabelShop bills** (the line total in cents), so the QB invoice matches LabelShop's
  subtotal. The SO line keeps 4 decimals on the rate, so 2,500 × 0.12345 is stored as rate 0.1235 with
  a line total of 308.625: the preview then warns "2,500 × 0.1235 = $308.75, but LabelShop bills
  $308.63 — key the amount, not the rate". A shipment-born invoice billing a different quantity is
  re-priced at the ordered quantity and warned.
- Warnings, not blockers: rate precision, charge with no keyword match, customer not linked to QB.
- `InvoiceLine.QbItemOverride` / `QbClassOverride` (migration `AddInvoiceLineQbOverrides`) are applied
  by the mapping and survive a draft rebuilt from the order (product lines by order line, charges by
  description).

## E10: Preview for QuickBooks

`/invoices/{id}/quickbooks`, reached from *Preview for QuickBooks* on the invoice and *QB preview* on
the Ready / Waiting lists. Laid out like the QB invoice form: customer (+ QB id, link), customer
email, terms, dates, bill-to / ship-to, invoice no., P.O. number, the lines, the shipping line and the
total before tax, with *copy* buttons on the values Sabrina types. Blockers are a red box at the top,
warnings yellow. Each line has item and class dropdowns that save at once (fetch, no reload); *Back to
mapping* clears an override; changing one never pins the other. Locked once exported, non-billable or
void.

## E11: Mark exported from the preview

*Mark exported to QuickBooks* on a Ready invoice; a Waiting invoice only offers *Mark exported anyway*
behind a required "It's already in QuickBooks" checkbox (the server refuses without it and lists the
reasons). It reuses `Invoice.MarkExported`, which already recorded when and who — `QbExportedAt` and
`QbExportedById` existed before this branch, so no migration — and returns to the Ready list with
"INV-… marked as exported to QuickBooks." The existing CSV exports are unchanged.

## E12: Die web-usage warning

The die form warns when labels across × label width covers under 60% of the die's web width ("3
across × 2.5 in = 7.5 in uses only 58% of the 13 in web. Check the die isn't entered sideways…"),
rendered from the saved values and live as the fields change. It never blocks a save.

## Migrations added after E5 (all additive; Up → Down → Up proven locally)

| Migration | Change | Existing rows |
|---|---|---|
| `AddInvoiceBillBeforeShip` | `Invoice.BillBeforeShip` bool NOT NULL DEFAULT false | false: wait for shipping as before |
| `AddCustomerQuickBooksSetup` | `Customer.QuickBooksCustomerId` varchar(100), `TaxSetupComplete` bool DEFAULT false, `TaxSetupCompletedAt` date | backfill: customers with an exported invoice → set up, dated by first export |
| `AddQuickBooksSettings` | new table `QuickBooksSettings` | none (no seed row; defaults in code) |
| `AddInvoiceLineQbOverrides` | `InvoiceLine.QbItemOverride`, `QbClassOverride` varchar(100) | null = use the mapping |

Startup on production: `MigrateAsync` applies the four in order; only `AddCustomerQuickBooksSetup`
writes data (one guarded UPDATE over Customer joined to an aggregate of exported invoices). The
seeders don't touch Customer, Invoice, InvoiceLine or QuickBooksSettings (`MasterDataSeeder` returns
early once the press and shipping methods exist).

## After Phase 0 (2026-11-05): shipments for Mark shipped, Billing contact

Branch `feature/ship-gate-billing-contact`. Prompted by Accounting finding nearly every invoice on
Waiting after Phase 0 deployed. Two causes:

- **Production's Mark shipped never recorded a shipment.** The job page / jobs list advanced the job
  to Shipped, but E6 counts an order as shipped only from recorded shipments, so those orders stayed
  In production and their invoices waited with no way out (Record shipment only took Rewound jobs,
  Mark order shipped refuses a line that shipped nothing).
- **Billing contacts were found by matching words in the free-text Role**, which nobody could see.

### Mark shipped records the shipment (or sends the order to Record shipment)

- `ShippingMethod.RequiresShipmentRecord` (Setup → Shipping methods, "Needs a shipment record").
  Defaults from the type: FedEx on; Pickup, Delivery, Customer account off. **An order with no
  method counts as on** — it bills as a carrier shipment (E4), so it needs the carrier cost
  (Thad, 2026-11-05).
- Moving a job to Shipped or Closed from below Shipped — job page next step, Record & mark shipped,
  jobs-list popup, status dropdown — goes through `JobService.RecordShipmentIfShippingAsync` →
  `ShipmentService.RecordJobShippedAsync`:
  - method off: the line's unshipped quantity goes on a package-less shipment, then the order
    status rolls up and the draft invoice's shipping syncs. Lines of one order marked shipped within
    `PackagelessShipmentWindow` (12 h, rolling, so it doesn't split at the UTC date line) share one
    shipment → one local delivery fee. The Shipments one-click Mark shipped uses the same window;
  - method on: `ShipmentRecordRequiredException`, the job stays Rewound. The UI offers **Record
    counts** (production keeps its packing counts) and **Record shipment** (Shipping/Admin) instead,
    and the Shipments one-click isn't offered;
  - the line already shipped in full: no shipment, the job just moves.
- Jobs already at Shipped/Closed with **nothing** for their line on a recorded shipment (the old
  Mark shipped): Record shipment now accepts those lines, the job page shows a warning with
  **Record shipment now** (method off) or **Record shipment** (method on), and the invoice's
  waiting reason names the job ("JOB-… was marked shipped with no shipment recorded — record the
  shipment from the job page"). A line that shipped *short* on a recorded shipment is still not
  re-offered — that is an under-run, finished with Mark order shipped.

### Billing contact

- `Contact.IsBillingContact`, a **Billing** checkbox next to Primary. Role is only a label.
- `CustomerBillingRules.ChooseBillingEmail`: the Billing contact with an email (primary first), else
  the **primary contact's** email (Thad, 2026-11-05). The invoice waits ("No billing email …") only
  when neither exists. The QB preview marks a fallback "(primary contact)" and warns; the sales
  order's Email invoice defaults to the same address.
- Fixed with it: rows added with Add contact / Add address saved Primary / Default unticked even when
  ticked (the hidden `false` twin preceded the checkbox and the binder takes the first value).

### Migrations (all on the branch; Up → Down → Up proven)

| Migration | Change | Existing rows |
|---|---|---|
| `AddShippingMethodRequiresShipmentRecord` | `ShippingMethod.RequiresShipmentRecord` bool DEFAULT false | true on MethodType 2 (FedEx) |
| `AddContactIsBillingContact` | `Contact.IsBillingContact` bool DEFAULT false | ticked on exactly the contacts the retired role-word rule matched (21-case fixture, 0 mismatches) |
| `BackfillPrimaryContactForBilling` | data only | a customer with no Billing/primary contact with an email and exactly one emailed contact gets that contact as primary (14-case fixture, 0 mismatches) |

### Deploy-day effect

- No invoice moves from Ready to Waiting: billing-word contacts keep billing through the checkbox.
- Invoices waiting only on the billing contact move to Ready where a primary contact with an email
  exists or the primary backfill creates one. Customers with several emailed contacts and none
  primary or Billing keep waiting until someone ticks one.
- Production loses Mark shipped on FedEx and no-method orders the moment it lands; those go through
  Record shipment (a tracking number per package; drop-ships use the vendor's tracking and freight).
- Jobs marked shipped the old way stay as they are until someone uses the job page's fix (or Bill
  before shipping / Mark exported anyway on the invoice). Nothing creates shipments on deploy.

## Phase 1 compatibility (Appendix A)

- `DocNumber` = the LabelShop invoice number (`INV-2026-00xxx`, within QBO's 21-character limit).
  Before each create, Phase 1 queries `Invoice WHERE DocNumber = …`, so the push is idempotent and
  catches invoices Sabrina already keyed by hand.
- Quantity = quantity **ordered**, not shipped. That is what Sabrina keys today.
- Item / class: the E8 mapping, with the per-line overrides from E9/E10. Phase 1 maps the stored names
  to `ItemRef` / `ClassRef` ids.
- Unit price precision: LabelShop keeps 4 or more decimals on big-run lines. If QBO rounds the
  rate, Phase 1 sends `Amount` (or adds a rounding line) so the totals match. The preview should
  show both the amount and the rate.
- Tax is not sent. Phase 1 sends `ShipAddr` and `BillAddr`, and QBO applies the customer's
  taxability and resale setup. `TaxSetupComplete` is the human gate for that.
- The Phase 0 states map onto Phase 1's `ExportStatus`:

  | Phase 0 | Phase 1 `ExportStatus` |
  |---|---|
  | Waiting, with reasons | Blocked: reason |
  | Ready | Ready |
  | `QbExportedAt` + who | Exported |
  | Non-billable | Closed, no QB record |

  Phase 1 adds `QbInvoiceId`, `QbSyncToken`, an export log, `HoldSend` / `SendToOverride` (for
  "unless the ticket says otherwise"), and an Error state.
- Voiding a LabelShop invoice voids the QB invoice in Phase 1. Nothing is ever deleted
  automatically.

## Open business questions (Andrew is confirming these with Sabrina/Janae; they don't block Phase 0)

1. **Carrier markup: always 1.5× with no minimum?** What's built is a single multiplier setting
   (1.5) with no minimum and no per-customer or per-carrier exception. A minimum charge would be one
   more setting.
2. **Terms precedence: LabelShop vs QuickBooks.** The preview shows LabelShop's `Customer.Terms`
   (default Net 30) and due date. If QB's terms win, the preview should say so instead.
3. **Where does the resale certificate live?** LabelShop has `TaxExempt` and now `TaxSetupComplete`,
   but nowhere to keep the certificate itself (that would be a document upload on the customer).
4. **Do $0 samples need a QB record?** What's built: no. A non-billable invoice closes without one
   (E5), and $0 invoices wait with "close it as non-billable or add charges".
5. **Are Design, Rush and Die Creation always separate charge lines?** E8 assumes so and classifies
   them by keywords in the description; anything else falls to Sales with a warning.

Decided while building (tell Andrew):

6. **Partial shipments and under-runs** (Thad): bill when the order is fully shipped — every line
   shipped in full, or the order marked Shipped. Under-runs are finished with *Mark order shipped* on
   the shipment page.
7. **Older shipments.** Shipments recorded before E2 get billed shipping worked out from their package
   cost × 1.5 when the order ships by carrier. The alternative is leaving those drafts at the quote.

New questions:

8. **Deploy day, customers.** Customers with an exported invoice were marked tax-setup complete; the
   rest wait on the new-client form. Right cut-off?
9. **Deploy day, billing contacts.** ~~Should the primary contact's email count as a fallback?~~
   Answered by Thad 2026-11-05: yes — see "After Phase 0" below.
10. **QuickBooks names.** Are the item names (Sales, Shipping, Design, Rush, Die Creation), the class
    names (Roll Label, Shrink Sleeves, Buyout) and the `OUTSOURCED` substrate code exactly as in QB /
    LabelShop? Sabrina can change them on Setup → QuickBooks mapping.
11. **Classes on mixed orders and shipping.** Charges and the shipping line take the first product
    line's class. Is that right when an order mixes roll labels and shrink sleeves, and should the
    shipping line carry a class at all?
12. **Rate precision.** Sales-order prices with 5 decimals are stored with 4, while the invoice keeps
    the exact line total (the preview says "key the amount, not the rate"). Should the SO form round
    prices to 4 decimals up front?
13. **Die warning threshold.** The 12 oz example (8" × 1-up on a 13" web) uses 61.5%, so the 60% rule
    doesn't flag it. Raise the threshold to 65–70%?
