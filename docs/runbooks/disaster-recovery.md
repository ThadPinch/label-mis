# Disaster Recovery Runbook

**Scenario:** Production server unavailable at 8:00 AM Monday.

## Immediate (0–15 min)

1. Confirm outage (ping app URL, check host/container status).
2. Notify shop: use read-only backup of old system if cutover not complete; otherwise hold new orders on paper.
3. Check Postgres: `pg_isready -h <host> -p 5432`.

## Restore database (15–60 min)

1. Latest backup: `/var/backups/labels-mis/labels_mis_YYYYMMDD.sql.gz` (nightly `pg_dump`).
2. Restore to standby or new instance:
   ```bash
   gunzip -c labels_mis_YYYYMMDD.sql.gz | psql -h localhost -U postgres -d labels_mis
   ```
3. Update connection string in deployment config.
4. Run pending migrations if restore is behind: `dotnet ef database update`.

## Restore application

1. Deploy last known-good container/image or `dotnet publish` artifact from CI.
2. Verify: login, open dashboard, one read-only query (customer list).
3. Smoke test: open a job, view an invoice.

## Post-incident

- Log root cause and duration.
- Verify backup from previous night restores cleanly (monthly drill).
- Do **not** add features during recovery.

## Contacts

- Admin user: see Identity seed (`IdentitySeeder.DefaultAdminEmail`)
- Postgres host: deployment-specific

## Backup schedule

- **Nightly** `pg_dump` at 02:00 local, retain **30 days**.
- Store off-server (S3 or NAS).
