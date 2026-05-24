# Labels MIS — Domain Reference

A synthesis of how the three dominant label-industry MIS platforms structure the business: **Cerm** (Belgian, narrow-web focused, ~50 Esko integrations), **Label Traxx** (Amtech/ePS, US-centric, dominant in mid-market label converters), and **Radius / Radius ERP** (ePS, enterprise packaging including labels, used by 9 of top 10 label printers globally).

This is the union of what they cover — what your MIS needs to *handle*, not what you need to ship in v1.

---

## 1. Core object: The Estimate

All three platforms agree the **estimate is the central object** of a label MIS. It encapsulates:

- Customer/prospect
- The manufacturing plan (raw materials, tooling, press route, finishing route)
- Estimated cost
- Sale price(s) at one or more quantities

The estimate becomes the template for the **product** (recurring spec) and the **job ticket** (one production run). Repeat orders re-use the product spec without re-estimating.

**Label Traxx terminology**: estimate → product → ticket (job)
**Cerm terminology**: estimate → product → sales order → job
**Radius terminology**: master estimate → estimate → sales order → job

### Quantity break logic
Estimates produce prices at multiple quantities simultaneously (e.g. 1k, 5k, 10k, 25k, 50k, 100k labels). Pricing is non-linear because setup costs amortize. Markup sets are configurable per customer type or market.

---

## 2. Entities — the master data

### Customers / Prospects
- Customer with billing/shipping addresses, contacts, terms, tax exempt info
- Prospect (pre-customer) — same shape, converts to customer on first order
- Customer-specific pricing rules, markup sets, default ship method
- Sales reps assigned (commission tracking)
- Distributor/Broker/Manufacturer Rep relationships (third-party intermediary who marks up and resells; needs separate visibility)

### Products
A **product** is a recurring label spec for a customer. Examples: "Tommy's Express 4x6 Carwash Window Cling", "Acme Pharmaceutical Bottle Label SKU-12345".

Product holds:
- Label dimensions (across × around, plus corner radius for rounded shapes)
- Substrate (face stock + adhesive + liner)
- Number of colors + ink set (CMYK, CMYK + spot, CMYK + white, CMYK + white + varnish)
- Specific ink colors (Pantone references or custom mixes)
- Print method (flexo, digital toner, digital inkjet, letterpress, screen, combination/hybrid)
- Finishing operations (laminate, varnish, foil, emboss, die-cut, sheeting, perforations, sequential numbering)
- Tooling references (die, plate set, foil dies)
- Roll specification (labels per roll, core size, unwind direction, OD max)
- Packaging spec (rolls per case, case label format)
- Artwork file references
- Customer PO number, SKU, internal SKU mapping

Products in stock = finished goods inventory (printed and held until customer calls off).

### Press / Equipment
A press record defines:
- Press name / asset number
- Type (flexo narrow-web, flexo mid-web, digital toner [HP Indigo], digital inkjet [Domino, Epson, Memjet], hybrid)
- Web width capability (min/max)
- Repeat length range (min/max, typically 6"-25" for narrow web)
- Color stations (number of print decks)
- Available inline modules (lamination, cold foil, hot foil, die-cut, sheet, slit, varnish)
- Speed (m/min or fpm at different substrates)
- Setup time / makeready waste norms
- Cost per hour (labor + overhead + recovery)
- Tooling compatibility (gear pitch, magnetic cylinder sizes)

The press model drives **estimating math**: which jobs run on which press, at what speed, with what waste, costing what per hour.

### Stocks / Substrates
Often the most complex master data. A stock record contains:
- Face material (paper: SC, BOPP, polyester, polyethylene, vinyl, foil, thermal transfer, direct thermal)
- Adhesive (permanent, removable, freezer, ultra-removable, repositionable)
- Liner (40# SCK, PET, glassine)
- Width (purchased — typically larger than press web width to allow trim)
- Cost per MSI (thousand square inches) or per linear foot
- Supplier(s) with vendor part numbers
- Minimum order quantity
- Lead time
- Compatible with which presses
- UV-resistant? Food-safe? FDA-compliant? Compliance flags

**Construction**: a derived/composed substrate — Label Traxx has "Stock Constructions" — face + adhesive + liner combination if buying components separately to laminate in-house.

### Tooling
The label industry has unusually heavy tooling. Each tool type:

**Dies** (cutting tools):
- Flexible die (magnetic, mounts to a magnetic cylinder)
- Solid die (one-piece, more durable, more expensive)
- Anvil cover (sacrificial)
- Each die has: customer (often customer-owned), shape, dimensions, repeat length, # across, die supplier, P/N, location in shop, last used date, usage count, retired flag

**Printing plates**:
- Photopolymer plates (flexo)
- Plate dimensions, thickness, durometer
- Plate set: one plate per color per repeat
- Stored mounted-or-unmounted on plate mounting tape
- Storage location
- Customer-owned typically

**Magnetic cylinders / print cylinders**: gear pitch determines repeat length. Each cylinder is one fixed repeat (e.g., a 3.5" repeat cylinder produces one repeat per revolution at 3.5"). Mark Andy, Nilpeter, etc. each have their own cylinder libraries.

**Foil dies, embossing dies, screen plates** — same tracking model.

**Tooling traceability is non-negotiable**: you must answer "when was this die last used, on what job, by whom" — required for FDA, pharma, and even general quality complaints.

### Inks
- Stock inks (standard CMYK, common Pantones)
- Custom mixes (per-customer color match, often given a customer mix code)
- White ink (heavier, more expensive, behaves differently)
- Specialty inks (metallic, fluorescent, security, scratch-off)
- Cost per pound + density (lbs/gallon)
- Anilox volume (BCM, cells per inch / LPI) compatible

### Suppliers / Vendors
- Stock suppliers (Avery Dennison, UPM Raflatac, Fasson, Mactac, Ritrama)
- Tooling suppliers (RotoMetrics, Wilson, Gerhardt — Label Traxx integrates with RotoMetrics for electronic die ordering)
- Plate suppliers (DuPont, Asahi, Flint)
- Ink suppliers
- Each with lead times, terms, EDI/email order method

---

## 3. Estimating math (the secret sauce)

A label estimate calculates **cost** then applies **markup** to get price. Cost = material + tooling + setup + run + finishing + overhead.

### Layout / imposition

Given:
- Label size (across × around)
- Gutter (gap between labels) — across and around, typically 1/16" to 1/8"
- Web width
- Repeat length (constrained by available cylinders for flexo, free-form for digital)
- Bleed

Calculate:
- Labels across the web = floor((web_width − edge_margin × 2 + across_gutter) / (label_across + across_gutter))
- Labels around the repeat = floor((repeat_length + around_gutter) / (label_around + around_gutter))
- Labels per impression = across × around
- Linear feet of web per 1,000 labels = (1000 / across) × repeat_length / 12

### Material consumption
- Web length needed = quantity / labels_across × (label_around + around_gutter) / 12 (feet)
- Add waste: setup waste (fixed feet, varies by press/job complexity, typically 200-800ft) + running waste % (typically 2-8%)
- Calculate material cost: feet × press_width × cost_per_MSI / 144

### Ink consumption (flexo)
Per color:
```
ink_area = label_area × coverage_pct × quantity
ink_volume_gal = ink_area × anilox_BCM × (1 / transfer_efficiency)
ink_cost = ink_volume_gal × density × cost_per_lb
```
Transfer efficiency typically ~25% for flexo (industry rule of thumb — ink left in anilox cells + plate). Anilox BCM (billion cubic microns per square inch) is the cell volume of the anilox roller. Higher coverage / heavier laydown → higher BCM.

### Ink consumption (digital)
Different math — cost per click model. HP Indigo charges per impression with click rates that depend on EPM (Enhanced Productivity Mode: 3 inks instead of 4 for same image), white ink usage ("can" charges), and the specific ink set. Label Traxx maintains this complexity per-Indigo-model.

### Run time
```
run_time_min = (web_feet / press_speed_fpm) + setup_time_min
```
Press speed isn't constant — it depends on substrate (films run slower than paper), coverage, and finishing inline.

### Plate cost
Plates are typically customer-owned but billed once on first order. Stored in tooling. Re-orders don't pay plate cost again unless plates are damaged.

### Die cost
Same as plates — typically customer-owned, billed once, stored. Re-orders skip die cost.

### Multi-up / combination / gang printing
Two different products from the same customer printed together to share setup. Cerm and Radius emphasize this; it's a major cost-saver for digital especially. The MIS needs to support:
- Detecting candidates (same substrate, same color set, compatible quantities)
- Cost-splitting logic (how does setup get allocated between the products on the gang)

---

## 4. Production workflow / job ticketing

Once an order is placed, the estimate becomes a **job ticket** (or "ticket" in Label Traxx, "job" in Cerm/Radius).

### Job ticket states (typical):
1. Estimate (no firm order yet)
2. Order entered / accepted
3. Prepress (waiting for artwork, in proofing, proof approved)
4. Scheduled (assigned to press, slotted)
5. In prepress production (plates being made)
6. Tooling order pending (if new dies/plates needed)
7. Materials staged
8. On press (running)
9. In finishing (off-press operations)
10. Inspection / QC
11. Packed / ready to ship
12. Shipped
13. Invoiced
14. Closed / archived

### Routing
A job has a **route** — an ordered list of operations on specific equipment. Label Traxx and Radius both emphasize this; tooling and stock are assignable to specific positions in the route.

Example route for a wine label:
1. Prepress → CTP plate making
2. Press 1 (Mark Andy P5, 7 colors + cold foil)
3. Off-line inspection / slitting (Rotoflex VSI)
4. Pack
5. Ship

### Scheduling
Three approaches in the industry:
- **Visual gantt** (drag-and-drop, manual) — Label Traxx classic, Cerm standard
- **Optimizer** (algorithmic, minimize setup time + meet due dates) — Cerm Scheduling Optimizer, Radius PrintFlow 4D, Label Traxx Batched
- **Hybrid** — algorithm proposes, scheduler adjusts

Optimizers cluster jobs to minimize:
- Substrate changes
- Width changes (re-slitting)
- Color washups
- Die changes
- Plate changes

### Shopfloor data collection (SFDC)
Operators clock onto jobs via terminal, tablet, or direct machine interface (DMI). The MIS captures:
- Start time / end time per job per machine
- Good count vs waste count
- Downtime with reason codes (mechanical, material, operator, setup, awaiting work)
- Speed throughout the run
- Operator identity (for traceability and labor costing)

Radius's "Auto-Count 4D" and Cerm's "Production Monitor" both connect directly to PLC counters on presses for automated capture. This data flows back into **job costing** (actual cost vs estimated) and **press performance** (running OEE).

---

## 5. Inventory

### Raw materials (roll stock)
Each roll has:
- Material ID (links to stock master)
- Unique roll ID (often barcoded, applied by supplier or on receipt)
- Width × length
- Lot / batch number (supplier's — critical for traceability)
- Receipt date
- Location (warehouse zone, rack, slot)
- Remaining quantity (decremented as used)
- Status (available, allocated, on press, returned to stock, scrapped)

Roll splits: when you cut a 13" roll into a 6.5" and a 6.5", you produce two new roll IDs and the original is consumed.

### WIP (work in process)
Jobs partway through have value tied up — printed but not yet die-cut, or printed but on hold for customer approval. Label Traxx's recent (2025) WIP tracking surfaces this. Needed for financial reporting.

### Finished goods (stock products)
Some products are produced to stock, not to order. Customer orders draw down stock. The MIS tracks:
- On-hand quantity
- Min/max reorder points
- Stock location
- Production triggered when below threshold
- Cost layer (FIFO/LIFO/avg)

### Consumables
Plates, dies (when not customer-owned), anvils, doctor blades, anilox rollers (asset-tracked), inks (drum-level), cleaning solvents. Lighter tracking but still in the system.

---

## 6. Purchasing / MRP

- Purchase orders generated from job material requirements
- Direct-to-vendor integrations:
  - Avery Dennison / Fasson stock ordering (Label Traxx has direct links)
  - UPM Raflatac
  - RotoMetrics for dies (electronic ordering with auto-quoting)
- MRP suggests POs based on:
  - Jobs in pipeline
  - Reorder points on common stocks
  - Supplier lead times
- Receipt → roll IDs created → inventory updated
- 3-way match: PO + receipt + invoice (for AP)

---

## 7. Finance / Accounting

All three platforms have integrated GL/AR/AP or tight external accounting integration:

- **AR**: invoices generated on ship, aged receivables, statements, credit holds
- **AP**: vendor invoices matched to POs and receipts
- **GL**: full chart of accounts, journal entries auto-posted from operational transactions, period close
- **Job costing**: estimated vs actual cost per job, variance analysis
- **Multi-currency**: required for international suppliers and customers
- **Multi-company / multi-site**: separate GLs per plant, intercompany transfers
- **Sales tax**: jurisdiction lookup, exempt certificates
- **Commissions**: tracked per sales rep / broker

Radius and Cerm have full internal accounting. Label Traxx has internal accounting that can also export to QuickBooks/external systems.

---

## 8. CRM

- Prospect → opportunity → quote → customer pipeline
- Sales rep assignment
- Task management with due dates
- Contact history (calls, emails, visits)
- Sales pipeline reports
- Integration with company email (Outlook, Gmail) for activity capture
- Cerm has "Lexis" — AI email processing that creates sales orders from inbound emails

---

## 9. Customer-facing portals (Web2Print / Web4Labels)

All three offer customer-facing portals:

- **Self-service reorder**: customer logs in, sees their products, places repeat orders
- **Online quote requests**: customer specifies dimensions, materials, quantity → system returns instant quote for standardized configurations, or routes to estimator for complex
- **Online proofing**: customer reviews artwork, approves or marks up
- **Order status**: customer sees where their jobs are in production
- **Reports**: customer sees their order history, spend, on-time delivery stats
- **Payment**: integrated payment for prepay customers

Cerm: Web4Labels. Label Traxx: Siteline (Customer Portal). Radius: customer portal as part of suite.

---

## 10. Prepress integration

The MIS does not do prepress (that's Esko, HYBRID, Kodak, etc.), but it **drives** prepress via integration:

- **JDF** (Job Definition Format) — XML standard that describes a job's prepress requirements
- The MIS exports JDF to prepress automation (Esko Automation Engine is the dominant target)
- Prepress sends back JMF (Job Messaging Format) status updates and the final ripped/imposed PDFs
- Hot folder integration — drop a die line file in a folder, MIS attaches it to the tooling record (Label Traxx feature)
- HP PrintOS integration for HP Indigo presses — comparing estimates to actuals shift-by-shift

---

## 11. Quality Management

- QC checklist per job (per product / per substrate / per customer)
- Test results captured (color delta-E, registration, dimensions)
- Pass/fail with reason codes
- Non-conformance tracking (re-runs, credits, scrap)
- Customer complaint linkage back to the original job → material lot → operator
- Required certifications (FDA, FSC, ISO) tracked per job / customer

---

## 12. Shipping

- Carrier integrations (FedEx, UPS, DHL, LTL freight)
- Label printing (shipping labels — yes, the MIS prints labels for the labels it makes)
- Tracking number capture and customer notification
- Packing slip generation
- BOL for freight
- EDI 856 (ASN) for big-customer compliance

---

## 13. Reporting / BI

- Standard reports: open orders, WIP, on-time delivery, press utilization, customer profitability, product profitability, scrap %
- Custom report writer (Label Traxx has "Super Reports", Cerm has Smart BI)
- KPI dashboards
- Data warehouse / API for external BI tools (Power BI, Tableau)
- All three platforms now offer some form of cloud-accessible BI separate from the transactional system

---

## 14. Multi-site / Enterprise

For converters with multiple plants:
- Shared customer master, separate inventory and GL per site
- Inter-site job transfers (start a job in plant A, finish in plant B)
- Centralized scheduling visibility
- Cross-site material/tool sharing with ownership tracking
- Consolidated reporting

---

## 15. Industry-specific concepts the MIS must understand

These will trip up a generic ERP every time:

- **Repeat length**: not the label height; it's the cylinder circumference. A label can repeat 2× per revolution on a long-repeat cylinder.
- **Across the web**: number of label columns. Driving factor in material efficiency.
- **MSI**: Thousand Square Inches — the standard pricing/costing unit for label stock in North America. (Europe uses m².)
- **Linear feet**: the natural unit of web consumption.
- **Liner / face / adhesive**: three layers of pressure-sensitive label stock.
- **Matrix**: the waste web after labels are die-cut out. Stripped and rewound separately.
- **Bleed**: image extends past die-cut line so trim variation doesn't show white edges.
- **Unwind direction**: 8 possible orientations of label on roll relative to copy. Critical for auto-applicators.
- **Core size**: 1", 3", 6" — must match customer's applicator.
- **OD (outer diameter)**: max roll size customer's machine can handle.
- **Splice**: when one roll of stock runs out and you tape onto a new roll — affects yield and is a defect risk.
- **Lamination**: applying a clear film over the print for durability/protection.
- **Cold foil vs hot foil**: cold foil is inline via UV adhesive, hot foil is a separate operation typically.
- **Sheeting**: cutting roll output into sheets (for sheet-fed customers).
- **Variable data / VDP**: each label different (sequential numbers, unique codes, mailing lists).
- **Versioning**: same base label, different language/SKU variations (12 versions × 5000 each = 60k total).
- **Color matching**: Pantone reference + delta-E tolerance + spectrophotometer reading on press.
- **Anilox / plate / cylinder** as separate tracked items each with their own life cycle.

---

## 16. Integrations — the "must support" list

Roughly in order of priority for a label MIS:

1. **Accounting** (QuickBooks, Sage, Microsoft Dynamics, NetSuite, Acumatica) — even if the MIS has internal accounting, export is often required
2. **Prepress automation** (Esko Automation Engine via JDF is the dominant standard)
3. **Press DFEs** (HP PrintOS for Indigo, Xeikon X-800, Domino, Durst, Mark Andy)
4. **Shopfloor data collection** (direct PLC, OPC-UA, or vendor-specific protocols)
5. **Shipping** (FedEx, UPS, DHL APIs)
6. **Stock suppliers** (Avery Dennison Fasson, UPM Raflatac — EDI or web service)
7. **Tooling suppliers** (RotoMetrics electronic die ordering)
8. **Ink dispensing systems** (GSE, ColorSat — for automated ink kitchen)
9. **Web2Print / customer portal**
10. **Email / CRM** (Outlook, Gmail)
11. **EDI** for big customers (850 PO, 856 ASN, 810 invoice)
12. **PrintTalk** — JDF dialect for buyer-printer transactions (less common but exists)

---

## 17. What each platform emphasizes differently

**Cerm**:
- Strongest narrow-web focus with deep workflow integration
- Esko ecosystem (50+ joint installations)
- "Master data first" — they push extensive setup of presses, materials, tooling so estimates are accurate from day one
- Scheduling Optimizer and Production Monitor as separate paid extensions
- AI email-to-order ("Lexis", 2026)
- Subscription model with ~6 month update cadence
- Belgian/European company; strong in EU labels market

**Label Traxx (Amtech / ePS)**:
- Built on 4D (Mac-origin database, now multi-platform); architecturally older but pragmatic
- Strongest in mid-market US label converters
- Module names: Foundation (Estimating, Order Management, Raw Materials Inventory, Job Costing, AR, Reporting), then optional Quality Control, AP, GL, Business Metrixx (BI), Cloud API, Data Warehouse, Auto Traxx (automation)
- Siteline (cloud sales portal + customer portal) — mobile-friendly
- Batched (algorithmic scheduling)
- HP PrintOS deep integration
- Recent push: multi-facility, WIP tracking, enhanced routing (2025)

**Radius (ePS)**:
- Enterprise focus — used in 400+ plants including 9 of top 10 label printers, MPS, etc.
- Multi-language, multi-currency, multi-company, multi-plant from the start
- Strongest in folding carton / flexible packaging, applied to labels at enterprise scale
- "Master estimate logic" — central concept where similar jobs derive from a parameterized template
- PrintFlow 4D scheduling, Auto-Count 4D SFDC, iQuote estimating as sister products
- Plastic tax calculations built in (European regulation)
- Architecture criticized as dated (2000s-era) but functionally complete
- Now under ePS umbrella with cross-product integration push

---

## 18. Implications for your build

If you're building a new labels MIS, the minimum viable schema must understand:

**Entities (the absolute floor)**:
`Customer`, `Address`, `Contact`, `Product`, `Estimate`, `EstimateLine` (qty break), `Job`, `JobOperation` (route step), `Press`, `Stock` (substrate master), `Roll` (inventory unit), `Tool` (die/plate/cylinder), `Ink`, `Supplier`, `PurchaseOrder`, `Receipt`, `Invoice`, `Shipment`, `User`, `Operator`.

**Concepts the schema must encode**:
- Across × around imposition with gutters → labels-per-impression
- Repeat length as a constrained value (cylinder library) or free (digital)
- Web width vs purchased roll width
- Multi-quantity pricing on a single estimate
- Tooling life cycle (created → in-use → retired) with usage history
- Product as a reusable spec, distinct from each job
- Job route as ordered operations on equipment
- Roll-level traceability (lot → roll → job)
- Estimated vs actual cost capture per job

**Things you can skip in v1 but need eventually**:
- Multi-site / multi-currency
- Full GL (use QuickBooks export)
- BI / data warehouse (Postgres views + Metabase early)
- Optimizer (manual scheduling gantt is fine for v1)
- EDI (until a customer demands it)
- Web2Print portal (until you have customers asking)

**Things you can never skip even in v1**:
- Estimate → product → job traceability
- Roll lot tracking
- Tooling history per customer
- Press routing with operation-level costing
- Material/setup/run/waste calculation
- Customer-specific markup / pricing

---

## 19. Reference glossary

- **MIS**: Management Information System. In print, ≈ ERP for printers.
- **ERP**: Enterprise Resource Planning. Broader, more financial.
- **JDF/JMF**: Job Definition Format / Job Messaging Format. XML standards for print workflow.
- **PrintTalk**: cXML-based standard for print procurement.
- **OEE**: Overall Equipment Effectiveness (availability × performance × quality).
- **DFE**: Digital Front End — the RIP/controller that drives a digital press.
- **CTP**: Computer-to-Plate, the prepress step of imaging a printing plate.
- **MSI**: Thousand Square Inches.
- **BCM**: Billion Cubic Microns (anilox cell volume).
- **LPI**: Lines Per Inch (anilox cell density, or halftone screen ruling).
- **ΔE / Delta-E**: color difference metric.
- **SFDC**: Shop Floor Data Collection.
- **MRP**: Material Requirements Planning.
- **WIP**: Work in Process.
- **ASN**: Advance Shipping Notice (EDI 856).
- **VDP**: Variable Data Printing.
- **PSA**: Pressure-Sensitive Adhesive.
- **SCK**: Super Calendered Kraft (a common liner).
- **BOPP**: Biaxially Oriented Polypropylene (film face stock).
