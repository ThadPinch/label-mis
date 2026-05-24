# Labels MIS

Management Information System for a narrow-web digital label shop running an HP Indigo 6800.

Built to replace spreadsheet-driven workflows with a real system: estimates, products, jobs, scheduling, inventory, shipping, invoicing.

## Status

Pre-alpha. Building out Tier 1 (MVP). See `docs/tier1-buildout.md` for the roadmap.

## Stack

.NET 10 · ASP.NET Core (Razor Pages) · EF Core 9 · PostgreSQL 16 · Docker

## Local development

```bash
# Start Postgres
docker-compose up -d

# Apply migrations
dotnet ef database update --project src/LabelsMis.Infrastructure --startup-project src/LabelsMis.Web

# Run
dotnet run --project src/LabelsMis.Web
```

App runs at `https://localhost:5001`. Default seeded admin: `admin@labels-mis.local` / `ChangeMe!2026` (change on first login).

## Tests

```bash
dotnet test
```

CI runs the same on every PR (`.github/workflows/ci.yml`).

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
