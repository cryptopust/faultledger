# Local development

Read [AGENTS.md](../../AGENTS.md) and the [current status](../../README.md).
Requirements: pinned .NET SDK, Docker with Linux containers, Docker Compose,
and PowerShell for governance verification. Do not use real secrets or data.

## Prepare local-only configuration

Copy the example only when no local `.env` exists. Windows PowerShell:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
```

POSIX shell:

```sh
test -e .env || cp .env.example .env
```

Edit `.env` to replace `CHANGE_ME_LOCAL_ONLY` with `faultledger_local_only`.
Never commit `.env`. The unchanged placeholder is intentionally not accepted by
API readiness. Compose requires a nonempty password variable; it does not know
whether a chosen value is appropriate. Use simple synthetic local values, not
production passwords or connection-string metacharacters. `.env.example` contains
only examples; .NET itself does not load it or `.env`.

## Compose API and PostgreSQL

```text
docker info
docker compose config --quiet
docker build --tag faultledger-api:local .
docker compose up -d --build --wait --wait-timeout 120
docker compose ps
```

The API depends on PostgreSQL's container health for initial startup. It still
probes PostgreSQL itself through its readiness endpoint. The named volume mounts
at `/var/lib/postgresql` for the pinned PostgreSQL 18 image. Published API and
database ports bind only to `127.0.0.1`; defaults are 8080 and 5432.

Verify both endpoints with `curl --fail` on a POSIX shell, or on PowerShell:

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:8080/health/live
Invoke-WebRequest -UseBasicParsing http://localhost:8080/health/ready
```

A ready instance returns `200 Healthy` from both. With PostgreSQL unavailable,
liveness should remain `200 Healthy` and readiness should become `503 Unhealthy`.
PowerShell's web cmdlet throws for 503; inspect its response rather than assuming
the process crashed. Health output deliberately contains no internal diagnostics.

```text
docker compose logs --tail 50 api postgres
docker compose down
```

Shutdown does not remove the named volume. Do not use `down --volumes` as routine
cleanup. Changing initialization credentials in `.env` does not change an already
initialized PostgreSQL role; keep settings consistent or perform an explicitly
authorized local database maintenance action. No migrations or business tables
are created here. The bootstrap DB administrator is local-only, not production
least privilege.

## Native API with Compose PostgreSQL

Start only PostgreSQL: `docker compose up -d --wait postgres`. Then set the native
.NET configuration explicitly, using the same local settings as Compose.

PowerShell:

```powershell
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=faultledger;Username=faultledger_dev;Password=faultledger_local_only'
dotnet run --project src/FaultLedger.Api --no-launch-profile --urls http://localhost:8080
```

POSIX shell:

```sh
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=faultledger;Username=faultledger_dev;Password=faultledger_local_only'
dotnet run --project src/FaultLedger.Api --no-launch-profile --urls http://localhost:8080
```

Do not simultaneously bind the Compose API and native API to the same port.
Database outages do not crash startup. Missing/invalid configuration starts the
host but keeps readiness unhealthy; configure it and recreate the host. The
native launch profile uses port 8080 without machine-specific Rider settings.

## Full validation and diagnosis

Use the root commands in [README](../../README.md) and `docker compose config
--quiet` / `docker build .` after setting the local environment. CI uses locked
restore, the same solution build/tests/format verification, the governance check,
Compose validation, and an image build. No deployment or registry push exists.

The three `RequiresDocker` tests use a disposable PostgreSQL container with
synthetic credentials, bounded startup, and automatic cleanup. They do not use
or delete the named development volume. Docker missing/unavailable is an error,
not a test skip. A three-minute container lifecycle watchdog is not simulated
business time; no arbitrary sleep coordinates a correctness assertion.

The refused-connection test holds an exclusively bound local socket without
listening, rather than racing another process for an assumed-unused port. The
container stop/restart test creates a new host using the container's current
mapped address after restart; it does not assume ephemeral port reuse. This is
host/connectivity lifecycle coverage, not proof of crash-safe business state.

For diagnostic work only, `dotnet test --filter 'Category!=RequiresDocker'`
exercises the remaining checks without containers. This is not the full validation
gate and must never be reported as PostgreSQL success. See the
[bootstrap validation record](bootstrap-validation.md) for known blockers.
