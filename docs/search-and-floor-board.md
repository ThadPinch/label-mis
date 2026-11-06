# Global search and the jobs floor board

Two navigation features that read existing data only — no schema of their own.

## Global search (`/search`)

- **Where:** the search box in the header (`Pages/Shared/_Layout.cshtml`, `wwwroot/js/global-search.js`).
  Typing calls `/search?handler=Suggest&q=…` (JSON, five hits per category); Enter with nothing
  highlighted opens `/search?q=…`, the full results page (25 per category, 100 with `&type=<key>`).
- **Service:** `Services/Search/GlobalSearchService.cs` — one EF query per category, every term must
  match one of the category's fields (`upper(col) LIKE '%TERM%'`). Categories: customers (incl.
  contacts' names, emails, phones), sales orders, jobs, estimates, invoices, shipments (incl. tracking
  numbers), products, purchase orders, rolls, dies, stocks, suppliers.
- **Permissions:** the page model maps each category to the policy its record pages carry
  (`Pages/Search/Index.cshtml.cs` → `CategoryPolicies`) and only queries the categories the user
  passes. A new searchable category needs an entry there, or nobody sees it.
- **Query shapes** (`Domain/Search/SearchQuery.cs`, unit-tested):
  - document numbers — `JOB-2026-00052`, `job 52`, `so#52`, `inv37`, `po 4471`, `R-2026-00012`.
    The prefix ranks its category first; it does not hide the others (`PO 4471` can be a customer PO).
  - a bare number — `52` ranks every `…-00052` above numbers that merely contain 52.
  - a label size — `2x3`, `1.5 x 1`, `2 × 3`: dies and products within ⅛″, either orientation.
- **Ranking** (`Domain/Search/SearchRanker.cs`, unit-tested): each record offers a number, a name,
  detail fields and related text; exact > prefix > word start > substring within each tier, and
  number > name > detail > related. Typing a customer's name lists the customer above its orders.
- **Jump:** a query naming exactly one record (a full number, or a prefixed number unique in its
  category) opens it directly — from Enter in the box, or from `/search?q=` without `&all=true`.
- **Scale:** sequential substring scans, fine for tens of thousands of rows. If history imports make
  it slow, add `pg_trgm` GIN indexes on the number/name columns before changing the queries.

## Jobs floor board (`/jobs`)

- **Page:** `Pages/Jobs/Index.cshtml(.cs)` with `_FloorCard`, `_FloorList`, `_FloorRoute`, `_FloorDue`,
  `_FloorActions`; script `wwwroot/js/job-floor.js`, styles `wwwroot/css/job-floor.css`.
- **Views:** *Board* (a column per live stage: Pre-press, Press, Finishing, Rewinding, Shipping,
  Outsourced) and *List* (sortable table), remembered in the `ls_jobs_view` cookie. Live views load
  every live job (cap `JobFloorService.MaxJobs`) so the find box and the due chips filter in the
  browser; *Shipped*, *Closed* and *All jobs* are history: paged and searched on the server.
- **Where a job is:** `Domain/Jobs/JobFloorLocator.cs` (unit-tested) turns the job status plus its
  route operations into a stage, a station/step (`Laminate`, `Queued for HP Indigo 6800`,
  `At <vendor>`, `Ready to ship`), a detail line (`Step 2 of 3`, `Expected Nov 4 — late`, the shipping
  method), who is clocked on (an open `JobTimeEntry`), and the route as done/current/upcoming steps.
  The stage's operation types: Queued → Press, Printed → Finishing, Finished → Inspection,
  Rewound → Pack/Ship. `Services/Jobs/JobFloorService.cs` loads the facts; job hits in the global
  search show the same location.
- **Due flags:** `Domain/Jobs/JobDue.cs`. "Today" is the browser's day: `site.js` stores the IANA
  time zone in the `ls_tz` cookie and `Services/ClientClock.cs` reads it (server clock fallback).
- **Refresh:** the board re-fetches its own URL every minute and swaps `#floor-results` in place,
  skipping while a popup or the dates panel is open or the tab is hidden.
- **Actions:** the next-step popup, Record shipment / Record counts and the ticket come from
  `_FloorActions` with the same rules as the stage pages; the popup handlers
  (`ActionPanel`, `RecordAdvance`, `CompleteTask`) are unchanged.
- `?focus=<jobId>` scrolls to a job's card and flashes it.
