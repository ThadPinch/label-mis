<!--
PR title format: <Verb> <object> in imperative present tense.
Examples: "Add Customer entity and migration", "Fix imposition rounding"
-->

## Task
<!-- Link or filename: .agent/tasks/NNN-slug.md -->

## What changed
<!-- One paragraph summary. What and why. -->

## Schema changes
<!-- If you added/edited migrations, list them here. Otherwise: "None." -->
- [ ] No schema changes
- [ ] Added migration: `<MigrationName>`
- [ ] Verified Up/Down both work locally

## Tests
<!-- What's covered. If tests were skipped, say why. -->

## Acceptance criteria
<!-- Copy from the task file, check each as you confirm it -->
- [ ] All acceptance criteria from the task file are met
- [ ] `dotnet build` clean (no warnings)
- [ ] `dotnet test` green
- [ ] No new TODO comments without an associated task
- [ ] AGENTS.md updated if a new convention was introduced

## Notes for reviewer
<!-- Anything non-obvious: decisions made, things deferred, places to look closely. -->
