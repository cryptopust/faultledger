# Explicit transfer migration and local exercise

Read [AGENTS.md](../../AGENTS.md), [local development](local-development.md),
and [transfer boundaries](../architecture/transfer-domain.md). Docker is required
for the real procedure. Do not install system-wide prerequisites implicitly.

## Apply the reviewed migration

The repository-local `dotnet-tools.json` pins dotnet-ef 10.0.7. Restore it from
the repository root; no global tool install is necessary. Set the same obvious
local-only database configuration used by Compose. .NET does not load `.env`.

```powershell
dotnet tool restore
dotnet restore --locked-mode
dotnet build --no-restore
docker compose up -d --wait postgres
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=faultledger;Username=faultledger_dev;Password=faultledger_local_only'
dotnet ef migrations script --project src/FaultLedger.Infrastructure --startup-project src/FaultLedger.Infrastructure --no-build --output TestResults/InitialTransferPersistence.sql
dotnet ef database update --project src/FaultLedger.Infrastructure --startup-project src/FaultLedger.Infrastructure --no-build
docker compose up -d --build --wait --wait-timeout 120
```

Create the ignored TestResults directory first if it does not exist. Adapt host,
port, database and local credentials to actual Compose settings. Inspect the SQL
before the database-update command. For POSIX shells use `export
ConnectionStrings__Postgres='...'` instead of PowerShell environment syntax.
No credentials are written into migration code or committed configuration.

The design-time factory reads environment configuration and does not contact the
database to generate a migration/script. API startup does not call EnsureCreated
or Migrate. Integration tests create empty isolated databases and apply the real
migration chain themselves; they never use the Compose development volume.

## Exercise the real API after migration

```powershell
$body = '{"clientReference":"order-1001","idempotencyKey":"order-1001-attempt","amount":125.50,"currency":"USD"}'
$transfer = Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/transfers -ContentType application/json -Body $body
Invoke-RestMethod -Uri "http://localhost:8080/api/transfers/$($transfer.id)"
docker compose exec postgres psql -U faultledger_dev -d faultledger -c 'SELECT id, amount, currency, state, provider_reference, version FROM transfers;'
docker compose down
```

Expected successful state is Accepted, not Completed. Repeating the sample POST
with its existing key returns 409. Do not change keys or repost to work around
an unknown result. GET performs no provider query or reconciliation. An error
after possible dispatch requires preserving uncertainty, not automatic resubmission.

Use new synthetic references only for genuinely different experiments after
examining existing state. Shutdown must not delete the named volume. The first
migration's Down removes the transfers table and its data; it is destructive,
not a routine production rollback strategy. Back up useful experiments before
any deliberately authorized database reset or destructive schema action.

The workstation's absent Docker currently blocks this runtime exercise. Offline
SQL generation and compile/model tests are not substitutes for applying the
migration or exercising these HTTP calls against PostgreSQL.
