# Finished-goods (shelf) inventory — design

Status: **proposed, not built**. Answers Asana requests *Roll inventory on shelf* (1217550084735539)
and *Knight Transportation* (1217550084735538). Written 2026-09-21 against `main` @ ffb9104.

## The problem

Production sometimes ends up with extra finished rolls/decals (overruns, a customer who orders the
same label repeatedly, a die-cut blank that several customers use). Today those sit on a shelf with
no record. When the same product is ordered again, CSRs have no way to see the stock exists, so the
job goes back through press and finishing as if from scratch. Tom's second request is a specific
case: Knight Transportation labels should be fulfilled from the shelf when they are there, and only
printed + sent to outsourced finishing when they are not.

## What already exists

- `Roll` is **raw substrate** inventory (barcode, lot, remaining linear feet). It is not finished
  goods and should not be overloaded to hold them.
- Every `SalesOrderLine` has a required `ProductId`, and `Product` has an optional `RollSpec`
  (labels per roll, core, unwind, max OD). That gives us a stable identity for "the same thing".
- `Job` tracks `GoodCount` per operation; the last finishing/rewind operation's good count is the
  real produced quantity.
- Jobs advance `PrePress → Queued → Printed → Finished → Rewound → Shipped → Closed`;
  outsourced jobs start at `Outsourced` and jump to `Rewound` on receipt.

## Recommendation

One small, product-keyed inventory that any customer can use — not a Knight-specific feature.

### Data

```
FinishedGoodsLot            (master data-ish; IsActive soft delete)
  Id, TenantId, audit cols
  ProductId        FK Product           — what it is
  CustomerId       FK Customer (null)   — who owns it (null = house stock usable by any customer
                                          assigned to the product)
  SourceJobId      FK Job (null)        — where it came from
  QuantityLabels   int                  — labels/decals. Rolls are derived from Product.RollSpec
                                          when present; labels are the unit of truth.
  Location         string?              — shelf/bin text, same idea as Roll.Location
  Notes            string?
  ReceivedAt       datetime
  QuantityRemaining int                 — maintained by consumption rows, never edited by hand

FinishedGoodsConsumption    (transactional, append-only)
  Id, TenantId, audit cols
  LotId            FK FinishedGoodsLot
  SalesOrderLineId FK SalesOrderLine
  Quantity         int                  — positive = consumed, negative = reversal/return
  Reason           enum { OrderFulfilment, Adjustment, Damaged, Reversal }
```

`QuantityRemaining` is a cached sum kept in the domain method (`Lot.Consume(qty)` throws if it
would go below zero). Auditability comes from the consumption rows, each tied to an order line.

### Workflow

1. **Put on shelf** — on Jobs → Detail, at `Rewound` or later, a "Put extra on shelf" action asks
   for a quantity (default: `TotalGoodCount − line.Quantity` when positive), location and notes;
   creates a `FinishedGoodsLot` for that job's product and customer. Also allowed from
   Production → Shipping when packing shows a surplus.
2. **See it when ordering** — on the sales order form, when a line's product is picked, the form
   fetches available lots for that product (customer-owned by this customer, or house stock) and
   shows "**On shelf: 3,200 labels (2 lots)**" beside the line with a **Use shelf stock** control.
   Nothing is deducted at this point.
3. **Consume** — when the order is *released to production* (the existing schedule/advance path
   that creates jobs), lines flagged to use shelf stock create consumption rows against the oldest
   lots first (FIFO). If the shelf covers the whole line, **no job is created** for it and the line
   goes straight to `Rewound`-equivalent (ready to ship). If it covers part, the job is created for
   the remainder only, and the ticket says "N from shelf, M to print".
4. **Reverse** — cancelling the order or editing the line quantity before ship writes a negative
   consumption row; the lot's remaining quantity comes back.

Deducting at release rather than at order entry avoids phantom reservations on orders that never
go anywhere, and is the same moment the system commits press time today.

### Knight Transportation

No special code path. Knight's products get lots on the shelf like anyone else's. What they
actually asked for — "if not in inventory, print ticket then outsourced finishing" — is the
existing outsourced-line routing (`SalesOrderLine.OutsourcedItem`, `JobStatus.Outsourced`)
applied to the remainder after shelf stock, which step 3 already yields. The one thing to confirm
with Tom: which Knight products are candidates, and whether they are house stock or
customer-owned. Do not key anything on the customer's name.

### UI surfaces

- Jobs → Detail: "Put extra on shelf" button (Rewound/Shipped/Closed).
- New page **Inventory → Finished goods**: list lots (product, customer, remaining, location,
  source job), adjust/damage/deactivate, and a per-lot history of consumption rows.
- Sales order form: per-line shelf availability readout + "Use shelf stock" toggle.
- Job ticket: "From shelf: N (lot …)" row when part of a line was fulfilled from stock.

### Sizing

Domain entities + config + one migration (two tables), one service, two page changes, one new
page, ticket line. Roughly the size of the outsourcing feature. Suggested as its own task file
(`.agent/tasks/0NN-finished-goods-inventory.md`) with this doc as the spec.

## Open questions for the shop

1. Unit of record: labels only (rolls derived) — confirm this is how they count on the shelf.
2. Is shelf stock ever *not* tied to a customer (true house stock)?
3. Who may put on shelf / consume / adjust — production only, or CSRs too?
4. Should the estimate (not just the order) show "on shelf" to steer pricing?
5. Knight: which products, and are they customer-owned?
