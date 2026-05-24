# Task 002: Estimating Engine

## Goal
Implement `LabelsMis.Domain.Estimating.EstimatingService` per the spec in
`docs/estimating-engine.md`.

## Context
- Read first: `docs/estimating-engine.md` (full spec, math, worked example)
- Read first: `docs/domain-reference.md` sections 3 and 15 (industry terminology and concepts)
- Read first: `AGENTS.md` (project conventions, DB migration rules, naming)
- Tier 1 phase: phase 2 (see `docs/tier1-buildout.md`)

## Acceptance criteria
- All types defined per the spec's "Inputs" and "Outputs" sections
- All calculation steps implemented per the spec's "Calculation flow" section
- 12 tests from the spec's "Test scenarios required" section pass
- The worked example in the spec reproduces to the cent
- Zero references to `LabelsMis.Infrastructure` or EF Core
- Zero references to `DateTime.Now`, `Random`, or any non-deterministic call
- All money as `decimal`, all dimensions as `decimal`, never `double` or `float`

## Out of scope
The "Open questions" section of the spec lists features the engine
explicitly does NOT handle in v1. Do not implement them, even if they
seem like natural extensions. Tier 2/3 work.

## Deliverables
- Files under `src/LabelsMis.Domain/Estimating/` per the spec's "Location in the codebase" section
- Tests under `tests/LabelsMis.Domain.Tests/Estimating/`
- Update `docs/estimating-engine.md` if any implementation decision differs from the spec — spec must stay in sync with code
- PR with all tests green in CI

## Notes
- Build this BEFORE any UI exists. The engine is a library called by the UI later.
- If a calculation seems ambiguous, surface it as a warning in the result,
  do not silently default. Better to confuse the estimator than to mislead them.
- The 12th test (five historical jobs within 2%) requires real data from the shop.
  Leave this test marked `[Skip("awaiting historical job data")]` until that data is provided.
