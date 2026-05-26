# Labels MIS

Management Information System for a narrow-web digital label shop running an HP Indigo 6800.

Built to replace spreadsheet-driven workflows with a real system: estimates, products, jobs, scheduling, inventory, shipping, invoicing.

## Status

**Tier 1 MVP implemented** — all nine build phases have code in place (master data through invoicing + cutover tools). Next step is shop cutover: parallel run, training, and data import. See `docs/runbooks/cutover-checklist.md`.

## Stack

.NET 10 · ASP.NET Core (Razor Pages) · EF Core 10 · PostgreSQL 16 · QuestPDF · Docker

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json` for pinned version)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for local PostgreSQL)

## Local development

```bash
# Clone and restore
git clone <repo-url>
cd Label-MIS
dotnet restore

# Start Postgres
docker compose up -d

# Apply migrations
dotnet ef database update --project src/LabelsMis.Infrastructure --startup-project src/LabelsMis.Web

# Run (use alternate port if 5000 is taken)
dotnet run --project src/LabelsMis.Web --urls "https://localhost:5001;http://localhost:5010"
```

App runs at the URL shown in the console. Default seeded admin: `admin@labels-mis.local` / `ChangeMe!2026` — you will be prompted to change the password on first login.

On startup the app applies pending migrations and seeds roles, the admin user, and the Indigo 6800 press when the database is empty.

## Tests

```bash
dotnet test
```

Integration tests require PostgreSQL — set `ConnectionStrings__Default` or use docker compose. CI runs build, test, and idempotent migration script validation (`.github/workflows/ci.yml`).

## Solution layout

```
src/
├── LabelsMis.Domain/          # entities, enums, estimating engine, domain rules
├── LabelsMis.Infrastructure/  # EF DbContext, migrations, FedEx/email clients
├── LabelsMis.Web/             # Razor Pages, services, PDF, background workers
└── LabelsMis.Tools/           # CLI CSV importers for cutover

tests/
├── LabelsMis.Domain.Tests/
├── LabelsMis.Infrastructure.Tests/
└── LabelsMis.Web.Tests/
```

## Application areas

| Module | What it does |
|--------|----------------|
| Master data | Customers, suppliers, stocks, dies, inks, finishing ops, presses |
| Estimates | Live-calculated quotes, PDF, revisions |
| Products & orders | Won estimate → product; repeat sales orders |
| Jobs | Schedule from order, operator tablet UI, job ticket PDF |
| Inventory | PO → receipt → roll barcodes, split, consume on job |
| Shipping | FedEx sandbox rates/labels, tracking poller |
| Invoicing | Invoice from shipment, payments, AR aging, QB CSV export |

## Cutover importers

```bash
dotnet run --project src/LabelsMis.Tools -- customers ./data/customers.csv
dotnet run --project src/LabelsMis.Tools -- stocks ./data/stocks.csv
dotnet run --project src/LabelsMis.Tools -- products ./data/products.csv
dotnet run --project src/LabelsMis.Tools -- opening-ar ./data/open-ar.csv
```

## Documentation

- `AGENTS.md` — project conventions (read first if you're contributing)
- `docs/architecture.md` — system diagram and layer rules
- `docs/schema-erd.md` — entity relationship diagram
- `docs/domain-reference.md` — label industry terminology and concepts
- `docs/tier1-buildout.md` — phase-by-phase build plan (with implementation status)
- `docs/estimating-engine.md` — full spec for the calculation engine
- `docs/runbooks/` — cutover checklist and disaster recovery
- `.agent/tasks/` — work orders for agentic development

## Working with AI agents

Tasks live in `.agent/tasks/NNN-slug.md`. The agent reads the task file, the docs listed in its Context section, and `AGENTS.md` before doing any work. PRs follow `.github/pull_request_template.md`.

## License

Private. All rights reserved.
