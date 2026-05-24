# Estimating Engine — Specification

The estimating engine is a pure-function calculation library that takes structured job parameters and returns structured cost and price output. It is the heart of the MIS. Everything else is CRUD around it.

## Design principles

1. **Pure functions, no side effects.** Engine takes input, returns output. No database, no logging, no HTTP. Logging happens at the caller.
2. **No EF dependency.** The engine lives in `LabelsMis.Domain/Estimating/` with zero references to `LabelsMis.Infrastructure`.
3. **Deterministic.** Same input always produces the same output. No `DateTime.Now`, no `Random`.
4. **All inputs explicit.** No hidden config or appsettings lookups. If a number affects the result, it is in the input.
5. **All decisions transparent.** The output includes a breakdown showing how the final price was reached, not just a single number. The estimator must be able to see why a price is what it is.
6. **Decimal everywhere.** Never float. Money is `decimal`. Dimensions are `decimal`.

## Location in the codebase

```
src/LabelsMis.Domain/Estimating/
├── EstimatingService.cs           ← entry point: Calculate(request) → result
├── Models/
│   ├── EstimateRequest.cs         ← input DTO
│   ├── EstimateResult.cs          ← output DTO
│   ├── EstimateLineItem.cs        ← one cost line in the breakdown
│   ├── QuantityBreakRequest.cs
│   └── QuantityBreakResult.cs
├── Calculations/
│   ├── ImpositionCalculator.cs    ← labels per impression, web length
│   ├── IndigoClickCalculator.cs   ← Indigo per-impression cost
│   ├── SubstrateCalculator.cs     ← stock cost
│   ├── FinishingCalculator.cs     ← off-line operations
│   ├── SetupCalculator.cs         ← fixed setup costs per quantity
│   └── WasteCalculator.cs         ← startup + running waste
└── Rules/
    ├── PressRules.cs              ← which presses can run what
    └── MarkupRules.cs             ← customer / quantity markup
```

Tests live in `tests/LabelsMis.Domain.Tests/Estimating/` mirroring this structure.

## Inputs

### EstimateRequest

```csharp
public record EstimateRequest(
    // Label geometry
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    decimal GutterAcrossIn,    // typical 0.0625 - 0.125
    decimal GutterAroundIn,    // typical 0.0625 - 0.125
    decimal BleedIn,           // typical 0.0625

    // Press selection
    Guid PressId,
    decimal PressWebWidthIn,
    decimal PressEdgeMarginIn, // unprintable edge, typical 0.125 each side
    decimal PressSetupMinutes,
    decimal PressCostPerHour,
    decimal PressSpeedFpm,
    bool PressClickBased,

    // Ink set (Indigo)
    IndigoInkSet InkSet,       // CMYK, CMYKW, CMYKW_Spot1, CMYKW_Spot2, EPM
    decimal ClickRatePer1000,  // for the chosen ink set
    bool WhiteInkUsed,
    decimal WhiteClickRatePer1000,

    // Substrate
    Guid StockId,
    decimal StockWidthIn,
    decimal StockCostPerMsi,

    // Finishing operations (ordered)
    IReadOnlyList<FinishingOperationRequest> FinishingOperations,

    // Quantities to price
    IReadOnlyList<int> Quantities,    // e.g. [1000, 5000, 10000, 25000, 50000]

    // Waste model
    decimal SetupWasteImpressions,    // Indigo: 15-50 typical
    decimal RunningWastePct,          // typical 0.02 - 0.05

    // Markup
    decimal CustomerMarkupPct,        // applied to total cost to get price
    decimal MinimumMarginPct          // floor margin — alert estimator if breached
);

public record FinishingOperationRequest(
    Guid OperationId,
    decimal SetupMinutes,
    decimal RunSpeedFpm,
    decimal CostPerHour,
    string Description
);

public enum IndigoInkSet
{
    EPM,                // 3-color, lowest click rate
    CMYK,
    CMYK_PlusSpot,
    CMYKW,              // CMYK + White
    CMYKW_PlusSpot
}
```

## Outputs

### EstimateResult

```csharp
public record EstimateResult(
    ImpositionResult Imposition,
    IReadOnlyList<QuantityBreakResult> QuantityBreaks,
    IReadOnlyList<string> Warnings,    // "white ink configured but not in ink set", etc.
    IReadOnlyList<string> Errors       // "label width exceeds press capacity" — caller should not proceed
);

public record ImpositionResult(
    int LabelsAcross,
    int LabelsAround,
    int LabelsPerImpression,
    decimal RepeatLengthIn,           // labels_around × (label_around + gutter_around)
    decimal UtilizationPct            // (labels_across × label_across) / press_web_width
);

public record QuantityBreakResult(
    int Quantity,
    int Impressions,
    decimal WebLengthFt,
    decimal RunTimeMinutes,
    decimal TotalCost,
    decimal TotalPrice,
    decimal UnitPrice,                // total_price / quantity
    decimal PricePerThousand,
    decimal MarginPct,
    bool BelowMinimumMargin,
    IReadOnlyList<EstimateLineItem> CostBreakdown
);

public record EstimateLineItem(
    string Category,                  // "Press click", "Substrate", "Laminate setup", etc.
    string Description,
    decimal Quantity,
    string Unit,                      // "impressions", "lf", "minutes", "ea"
    decimal UnitCost,
    decimal LineCost
);
```

## Calculation flow

The engine processes one request and returns results for every quantity in the request. Here is the math, step by step, for a single quantity.

### Step 1: Imposition

```
labels_across = floor(
    (press_web_width - 2 × press_edge_margin + gutter_across)
    / (label_across + gutter_across)
)

labels_around = 1  // for v1, single label around per impression on Indigo
                  // (the 6800 is sheet-fed-equivalent — one "impression" is one cut sheet's worth)

labels_per_impression = labels_across × labels_around

repeat_length = labels_around × (label_around + gutter_around)

utilization_pct = (labels_across × label_across) / press_web_width
```

**Validation**:
- If `labels_across < 1` → error "Label too wide for press"
- If `label_across + 2 × press_edge_margin > press_web_width` → error
- If `utilization_pct < 0.50` → warning "Low web utilization, consider gang or different press"

### Step 2: Impressions needed

```
overrun_factor = 1.0 + (running_waste_pct)
impressions = ceiling((quantity × overrun_factor) / labels_per_impression) + setup_waste_impressions
```

### Step 3: Press cost

**Click cost** (if `press_click_based`):
```
click_cost = (impressions / 1000) × click_rate_per_1000

if white_ink_used:
    white_click_cost = (impressions / 1000) × white_click_rate_per_1000
    click_cost += white_click_cost
```

**Substrate cost**:
```
total_web_length_in = impressions × repeat_length
total_web_length_ft = total_web_length_in / 12
total_msi = (impressions × repeat_length × stock_width_in) / 1000
substrate_cost = total_msi × stock_cost_per_msi
```

**Press time and labor**:
```
press_run_minutes = total_web_length_ft / press_speed_fpm × 60
press_total_minutes = press_setup_minutes + press_run_minutes
press_labor_cost = (press_total_minutes / 60) × press_cost_per_hour
```

### Step 4: Finishing cost

For each finishing operation in order:
```
op_run_minutes = total_web_length_ft / op_run_speed_fpm × 60
op_total_minutes = op_setup_minutes + op_run_minutes
op_cost = (op_total_minutes / 60) × op_cost_per_hour
```

Sum across all operations → `total_finishing_cost`.

### Step 5: Total cost

```
total_cost =
    click_cost
    + substrate_cost
    + press_labor_cost
    + total_finishing_cost
```

### Step 6: Pricing

```
total_price = total_cost × (1 + customer_markup_pct)
unit_price = total_price / quantity
price_per_thousand = unit_price × 1000
margin_pct = (total_price - total_cost) / total_price
below_minimum_margin = margin_pct < minimum_margin_pct
```

### Step 7: Build the breakdown

Every cost component above becomes one `EstimateLineItem` in `CostBreakdown` for transparency.

## Worked example

Customer wants 25,000 4x3" labels, CMYK on white BOPP, with overlaminate, on the Indigo 6800.

**Input**:
- Label: 4.0" × 3.0", 0.125" corner radius, 0.0625" gutters, 0.0625" bleed
- Press: Indigo 6800, web_width 13", edge_margin 0.25", setup 20 min, $150/hr, speed running 100 fpm equivalent, click-based
- Ink set: CMYK, click rate $35/1000 impressions
- White ink: not used
- Substrate: 2.0 mil white BOPP perm/40SCK, 13.5" stock width, $0.85/MSI
- Finishing: gloss laminate (setup 15 min, 200 fpm, $90/hr) + rotary die-cut/matrix strip (setup 30 min, 250 fpm, $110/hr)
- Quantities: [5000, 10000, 25000]
- Setup waste: 30 impressions
- Running waste: 3%
- Customer markup: 45%
- Minimum margin: 25%

**Step 1 — Imposition**:
```
labels_across = floor((13.0 - 0.5 + 0.0625) / (4.0 + 0.0625))
              = floor(12.5625 / 4.0625)
              = floor(3.09)
              = 3

labels_around = 1
labels_per_impression = 3
repeat_length = 1 × (3.0 + 0.0625) = 3.0625"
utilization = (3 × 4.0) / 13.0 = 92.3%  (good)
```

**Step 2 — Impressions for 25k**:
```
impressions = ceiling((25000 × 1.03) / 3) + 30
            = ceiling(8583.33) + 30
            = 8584 + 30
            = 8614
```

**Step 3 — Press cost**:
```
click_cost = (8614 / 1000) × 35.00 = $301.49

substrate:
  total_web_length_in = 8614 × 3.0625 = 26,380"
  total_web_length_ft = 2198.3 ft
  total_msi = (8614 × 3.0625 × 13.5) / 1000 = 356.2 MSI
  substrate_cost = 356.2 × 0.85 = $302.77

press_run_minutes = 2198.3 / 100 × 60... wait, that's wrong.
```

**Stop — units check**:
Press speed is typically given in fpm (feet per minute), so:
```
press_run_minutes = total_web_length_ft / press_speed_fpm
                  = 2198.3 / 100
                  = 21.98 min
```

(Don't multiply by 60 — fpm already encodes "per minute".)

```
press_total_minutes = 20 + 21.98 = 41.98 min
press_labor_cost = (41.98 / 60) × 150 = $104.95
```

**Step 4 — Finishing**:
```
laminate_run = 2198.3 / 200 = 10.99 min
laminate_total = 15 + 10.99 = 25.99 min
laminate_cost = (25.99 / 60) × 90 = $38.99

diecut_run = 2198.3 / 250 = 8.79 min
diecut_total = 30 + 8.79 = 38.79 min
diecut_cost = (38.79 / 60) × 110 = $71.12

total_finishing_cost = $110.11
```

**Step 5 — Total cost**:
```
total_cost = 301.49 + 302.77 + 104.95 + 110.11 = $819.32
```

**Step 6 — Pricing for 25k**:
```
total_price = 819.32 × 1.45 = $1,188.01
unit_price = 1188.01 / 25000 = $0.0475 per label
price_per_thousand = $47.52
margin_pct = (1188.01 - 819.32) / 1188.01 = 31.0%
below_minimum_margin = false  (31.0% > 25.0%)
```

That's the math. Same logic applied to 5k and 10k produces the quantity breaks. Setup costs dominate small quantities — that's why per-unit pricing drops as quantity rises.

## Test scenarios required before this engine is "done"

The engine ships when these 12 tests pass:

1. **Happy path**: standard CMYK job, 3 quantities, all calculations correct to the cent
2. **EPM mode**: 3-color produces lower click cost than CMYK on same input
3. **CMYKW**: white ink adds separate click cost line
4. **Label too wide**: returns error, no result calculated
5. **Low utilization warning**: 2" label on 13" press triggers warning but still calculates
6. **No finishing**: bare-print job, no laminate/die — calculates fine, finishing cost is $0
7. **Multiple finishing ops**: laminate + diecut + slit, each shows in breakdown
8. **Below minimum margin**: high cost vs low markup → `BelowMinimumMargin = true`
9. **Quantity break inversion check**: per-unit price at higher qty must always be ≤ per-unit price at lower qty (otherwise something is wrong)
10. **Setup waste impact**: 100 labels vs 100k labels — setup waste dominates small quantities
11. **Rounding**: 25,001 labels does not produce a wildly different price than 25,000
12. **Five historical jobs**: real estimates from the shop are reproduced within 2% of original quoted price

## Open questions the engine intentionally does not handle

These are tier 2/3 features. Leave hooks in the input schema but do not calculate:

- Gang printing (two products on one press run)
- Variable data / versioning (each label different)
- Multiple substrate options on one estimate
- Customer-owned vs shop-owned die fee amortization
- Plate cost amortization (Indigo doesn't have plates — irrelevant for v1)
- Volume discounts beyond quantity breaks
- Rush fees / express turn fees
- Re-runs and re-makes (treat as new estimate)
- Sample/proof charges
- Freight buildup (handled in shipping module, not estimating)

## Agent task framing

This entire document is the spec for the agent task `002-estimating-engine.md`. The task file should be a short pointer to this spec plus acceptance criteria:

```markdown
# Task 002: Estimating Engine

## Goal
Implement `LabelsMis.Domain.Estimating.EstimatingService` per the spec in
`docs/estimating-engine.md`.

## Context
- Read first: docs/estimating-engine.md (full spec, math, worked example)
- Read first: docs/domain-reference.md sections 3 and 15 (industry terminology)
- Read first: AGENTS.md (project conventions)
- Tier 1 phase: phase 2 (see docs/tier1-buildout.md)

## Acceptance criteria
- All types defined per the spec's "Inputs" and "Outputs" sections
- All calculation steps implemented per the spec's "Calculation flow" section
- 12 tests from "Test scenarios required" section pass
- Worked example in the spec reproduces to the cent
- Zero references to LabelsMis.Infrastructure or EF
- Zero references to DateTime.Now, Random, or any non-deterministic call
- All money as decimal, all dimensions as decimal

## Out of scope
The "Open questions" section of the spec lists features the engine
explicitly does NOT handle in v1. Do not implement them.

## Notes
- Build this BEFORE any UI exists. The engine is a library.
- If a calculation seems ambiguous, surface it as a warning in the result,
  do not silently default. Better to confuse the estimator than to mislead them.
```
