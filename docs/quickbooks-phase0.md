# QuickBooks Phase 0 (Track E): design choices

Status: **E1–E5 built** on `feature/epm-qb-phase0` (not pushed, not deployed). **E6 started**: the
unfinished work is parked on the local branch `wip/e6-ready-waiting`. **E7–E11 and the optional E12
are not started.** Their designs below are the decisions Thad approved on 2026-11-01. Written
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

## E6: Ready vs Waiting (started, unfinished)

Rule: an invoice is **Ready** when all of these hold:

- the order has shipped or been picked up, **or** the invoice is flagged `BillBeforeShip`;
- total > 0;
- it is not non-billable;
- nothing blocks it.

Everything else is listed on a **Waiting** tab with the reasons. The reasons on
`wip/e6-ready-waiting` (`InvoiceReadinessRules`) are:

- "Not shipped yet (x of y lines)". This uses the same per-line test as today's "Shipped x/y"
  column: a line counts as shipped when its shipped quantity ≥ its ordered quantity.
- "Nothing on the order ships — use Bill before shipping" (charge-only orders).
- "$0 total — close it as non-billable or add charges".
- "Waiting on carrier cost (SHP-…)", from the E4 pending rule.
- Customer blockers, which E7 fills in.

Still to do: the `AddInvoiceBillBeforeShip` migration (bool, NOT NULL, default false), the
Index tabs, the Bill-before-shipping toggle on the invoice page, and making bulk Mark exported and
CSV export work only from Ready. Then tests, the Up → Down → Up proof and a smoke test.

**Design point to settle before finishing:** the quantity test leaves a line that was
**under-run** (shipped below the ordered quantity, which is normal in label printing) waiting
forever. The alternative is to count a line as shipped once any non-pending shipment covers it,
or once the SO reaches Shipped / Invoiced / Closed. Partial shipments raise the same question.

## E7–E11: approved design (not started)

- **E7: Customer QB fields.**
  - New customer fields: `QuickBooksCustomerId` (text, nullable), `TaxSetupComplete` (bool) and
    `TaxSetupCompletedAt` (date). They go on the customer edit form.
  - Blockers:
    - "Waiting on new-client form" while tax setup is not complete;
    - "No billing contact with email" when no contact has the Billing role and an email address.
  - These plug into `GetCustomerBlockersAsync`, which E6 already stubs.
- **E8: Setup → QuickBooks mapping.**
  - One QB item per line type:
    - Sales (product lines);
    - Shipping;
    - Design, Rush and Die Creation (these are `SalesOrderCharge` lines today).
  - Class rule:
    - substrate `OUTSOURCED` → Buyout;
    - shrink stock → Shrink Sleeves;
    - otherwise Roll Label;
    - charge lines inherit the job's class.
  - A pure domain helper returns `(item, class)` for any invoice line.
  - Store QB **names** now. Phase 1 adds the QB ids (`ItemRef` / `ClassRef`) next to them.
  - Charge lines don't have a type today. The mapping will match the charge description against
    keywords that can be edited in Setup, or add a charge type. Decide this once the business
    question below is answered.
- **E9: QB-shaped view model and a JSON endpoint per invoice.**
  - The model holds:
    - `DocNumber` = the LabelShop invoice number;
    - the customer (name + QB id);
    - the PO (`CustomerPoNumber`);
    - one row per line: item, class, description, **quantity ordered**, unit price, amount;
    - the shipping line;
    - terms and due date;
    - ship-to and bill-to addresses;
    - the billing email;
    - the blockers.
  - Each invoice line gets nullable `QbItemOverride` / `QbClassOverride`, applied when set.
- **E10: "Preview for QuickBooks" page.** It renders the E9 model like a QB invoice, shows
  blockers in red at the top, and has inline dropdowns for the per-line overrides. Sabrina keys
  from this page until Phase 1.
- **E11: Mark exported from the preview.** It reuses `Invoice.MarkExported`, which already
  records who and when (`QbExportedAt`, `QbExportedById`). The existing CSV export stays as it is.
- **E12 (optional):** a warning on the die form when labels across × label width uses less than
  60% of the web width.

## Phase 1 compatibility (Appendix A)

- `DocNumber` = the LabelShop invoice number (`INV-2026-00xxx`, within QBO's 21-character limit).
  Before each create, Phase 1 queries `Invoice WHERE DocNumber = …`, so the push is idempotent and
  catches invoices Sabrina already keyed by hand.
- Quantity = quantity **ordered**, not shipped. That is what Sabrina keys today.
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
2. **Terms precedence: LabelShop vs QuickBooks.** LabelShop stores `Customer.Terms` (default
   Net 30). Which wins when that differs from the customer's terms in QB? E9 would show LabelShop's
   terms and due date. If QB's terms win, the preview should say so instead of showing a due date.
3. **Where does the resale certificate live?** LabelShop has a `TaxExempt` flag on the customer but
   nowhere to keep the certificate. Phase 0 only adds the `TaxSetupComplete` flag (E7). If the
   certificate should be stored in LabelShop, that means a document upload on the customer.
4. **Do $0 samples need a QB record?** What's built: no. A non-billable invoice closes without one
   (E5), and the E6 rule keeps $0 invoices out of Ready. If QB does need a $0 record, $0 invoices
   should go to Ready instead.
5. **Are Design, Rush and Die Creation always separate charge lines?** E8 assumes they are, with
   one QB item per line type. If they are sometimes folded into the label price, mapping by line
   type can't separate them.

Questions that came up while building:

6. **Partial shipments and under-runs.** Should an order be billed when its first shipment goes,
   or only when everything has shipped? And does a line shipped short of the ordered quantity count
   as shipped? (See the E6 design point above.)
7. **Older shipments.** For shipments recorded before E2, billed shipping is worked out from their
   package cost × 1.5 when the order ships by carrier. Is that acceptable for the drafts open at
   deploy time? The alternative is to leave those at the quoted amount.
