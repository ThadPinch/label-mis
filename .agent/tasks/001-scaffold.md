# Task 001: Project Scaffold

## Goal
Stand up an empty but runnable .NET 10 solution with Postgres, EF, Identity, CI, and a working "hello world" admin dashboard. No business logic.

## Context
- Read first: `AGENTS.md` — full project conventions
- Read first: `docs/tier1-buildout.md` Phase 0
- Read first: `README.md` for the stack

## Acceptance criteria

### Solution structure
- [ ] `global.json` pinning .NET 10 SDK
- [ ] `LabelsMis.sln` with three src projects and three test projects per `AGENTS.md` "Project structure"
- [ ] Project references enforce the dependency rule (Web → Infrastructure → Domain, never reverse)
- [ ] `.editorconfig` at root with consistent C# formatting
- [ ] `.gitignore` for .NET (use the standard GitHub .NET .gitignore template)

### Database
- [ ] Npgsql + EF Core 9 packages added to Infrastructure
- [ ] `LabelsMisDbContext` in `Infrastructure/Persistence/` with no entities yet
- [ ] `appsettings.json` and `appsettings.Development.json` with connection string for local Docker Postgres
- [ ] `dotnet ef database update` works against the local Docker Postgres
- [ ] One empty initial migration committed (`InitialCreate`)

### Auth
- [ ] ASP.NET Identity wired up with Postgres store
- [ ] Roles seeded: Admin, Estimator, CSR, Scheduler, Operator, Shipping, Accounting
- [ ] One admin user seeded: `admin@labels-mis.local` / `ChangeMe!2026`
- [ ] Login page works
- [ ] Force password change on first login

### Web app
- [ ] Empty dashboard page at `/` (authenticated route, redirects to login if not signed in)
- [ ] Shows logged-in user's name and roles
- [ ] Logout works
- [ ] Layout includes a nav placeholder (empty for now)

### CI / dev infra
- [ ] `docker-compose.yml` at root (already provided — verify it works)
- [ ] `.github/workflows/ci.yml` runs and passes on first PR (already provided — verify it works)
- [ ] `.github/pull_request_template.md` present (already provided)
- [ ] `README.md` reflects actual setup steps

### Tests
- [ ] One trivial test per test project that asserts `true == true`, just to confirm test discovery works
- [ ] CI runs them and they pass

## Out of scope
- Any entities beyond Identity tables
- Any business pages
- Any styling beyond the default Bootstrap that Razor Pages scaffolds
- External services (FedEx, QuickBooks)

## Deliverables
- PR with the scaffold
- CI green
- Branch: `agent/001-scaffold`

## Notes
- Use `dotnet new sln` + `dotnet new classlib/webapp/xunit` rather than Visual Studio's scaffolding so the result is reproducible from terminal
- Pin all package versions in `Directory.Packages.props` for central package management
- Do NOT install MediatR, AutoMapper, Serilog, or any other library beyond what `AGENTS.md` lists. Boring is good.
