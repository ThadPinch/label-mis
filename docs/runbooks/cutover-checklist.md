# Cutover Checklist

## Pre-cutover (T-2 weeks)

- [ ] All Tier 1 acceptance criteria verified in staging
- [ ] Six users completed role walkthroughs
- [ ] Quick-reference cards at workstations
- [ ] Parallel run: every order in old + new system
- [ ] Daily discrepancy report reviewed
- [ ] Bug log: zero blockers open

## Data migration (cutover morning)

Run importers in order (fresh CSV exports from old system):

```bash
dotnet run --project src/LabelsMis.Tools -- customers /path/customers.csv
dotnet run --project src/LabelsMis.Tools -- stocks /path/stocks.csv
dotnet run --project src/LabelsMis.Tools -- products /path/products.csv
dotnet run --project src/LabelsMis.Tools -- opening-ar /path/open-ar.csv
```

Import open sales orders manually or extend Tools (products + customers must exist first).

## Cutover day

- [ ] Final importer run with morning exports
- [ ] All users log in and confirm access
- [ ] Old system → read-only
- [ ] New system = system of record
- [ ] Backup verified this week

## Post-cutover (90 days)

- [ ] Old system retained read-only for 90 days
- [ ] Tier 2 ideas logged in `.agent/tasks/tier2/` — no scope creep during stabilization

## Parallel run success criteria

- Discrepancy rate **< 2%** over two weeks
- No blocker bugs open
