# Labels MIS

Management Information System for a narrow-web digital label shop running an HP Indigo 6800.

Built to replace spreadsheet-driven workflows with a real system: estimates, products, jobs, scheduling, inventory, shipping, invoicing.

## Status

Pre-alpha. Building out Tier 1 (MVP). See `docs/tier1-buildout.md` for the roadmap.

## Stack

.NET 10 · ASP.NET Core (Razor Pages) · EF Core 10 · PostgreSQL 16 · Docker

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

# Run
dotnet run --project src/LabelsMis.Web
```

App runs at `https://localhost:5001` (or the URL shown in the console). Default seeded admin: `admin@labels-mis.local` / `ChangeMe!2026` — you will be prompted to change the password on first login.

On startup the app also applies pending migrations and seeds roles plus the default admin user when the database is empty.

## Tests

```bash
dotnet test
```

CI runs the same on every PR (`.github/workflows/ci.yml`).

## Solution layout

```
src/
├── LabelsMis.Domain/          # entities, value objects, business rules
├── LabelsMis.Infrastructure/  # EF DbContext, migrations, external clients
└── LabelsMis.Web/             # Razor Pages UI

tests/
├── LabelsMis.Domain.Tests/
├── LabelsMis.Infrastructure.Tests/
└── LabelsMis.Web.Tests/
```

## Documentation

- `AGENTS.md` — project conventions (read first if you're contributing)
- `docs/domain-reference.md` — label industry terminology and concepts
- `docs/tier1-buildout.md` — phase-by-phase build plan
- `docs/estimating-engine.md` — full spec for the calculation engine
- `.agent/tasks/` — work orders for agentic development

## Working with AI agents

Tasks live in `.agent/tasks/NNN-slug.md`. The agent reads the task file, the docs listed in its Context section, and `AGENTS.md` before doing any work. PRs follow `.github/pull_request_template.md`.

## License

Private. All rights reserved.
