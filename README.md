# FaultLedger

A deterministic .NET engineering failure laboratory for studying distributed
financial-style orchestration. It is not a payment processor, bank, wallet,
real-money service, or compliance-certified platform. Synthetic data only.

## Current stage: repository/tooling bootstrap (0B)

Implemented files establish repository governance, an eight-project .NET 10
solution, explicit dependency boundaries, two API health endpoints, PostgreSQL
Compose configuration, a multi-stage API Dockerfile, centralized locked package
dependencies, a CI workflow, and architecture/HTTP/PostgreSQL test infrastructure.

**Validation boundary:** local restore, build, architecture tests and the negative
HTTP health regressions have passed. Docker/WSL are absent on the bootstrap
workstation: the three real-PostgreSQL tests fail their Docker prerequisite rather
than skip. Compose configuration was validated with the official standalone
Compose binary, but container image builds and Compose runtime behavior are not
yet demonstrated here. CI is configured, not claimed to have run remotely.
See the [validation record](docs/runbooks/bootstrap-validation.md) for exact results.

Not implemented: Money, Transfer, state machines, idempotency, provider simulation
or submission, UNKNOWN outcomes, reconciliation, callbacks, durable inbox, outbox,
audit trail, Redis, Toxiproxy, or application OpenTelemetry instrumentation. There
is no application database schema or migration. Empty Domain/Application projects
are deliberate; no placeholder business classes or fake tests were added.

## Prerequisites

Use SDK **10.0.203** from [global.json](global.json). Its exact pin remains active
under existing governance; this stage does not relax the roll-forward checker.
Rider 2026.2.1 is installed on the development workstation. Open
`FaultLedger.slnx`; CLI solution loading/build/discovery are verified separately
from unverified interactive Rider behavior.

Docker with Linux containers and Docker Compose are required for the full test
suite and local stack. Tests use isolated Testcontainers PostgreSQL instances,
not the Compose development database. PowerShell runs the governance verifier.

## Validate locally

From the repository root, use the same application commands as CI:

```text
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --no-restore
dotnet test --no-build --no-restore
dotnet format --verify-no-changes --no-restore
git diff --check
git status --short
```

Plain root `dotnet build` and `dotnet test` also select the complete solution.
On Windows run `powershell -NoProfile -File scripts/Verify-Governance.ps1`;
on PowerShell 7 use `pwsh` instead. Do not use `-BootstrapOnly` after stage 0A.
No test filter is used by CI or the completion gate. A missing Docker engine is
not a passing database suite.

## Run the local stack

Follow the [local development runbook](docs/runbooks/local-development.md) to copy
`.env.example` without overwriting existing settings, choose an obvious local-only
password, and start the API plus PostgreSQL. No real secrets belong here.

```text
docker compose config --quiet
docker compose up -d --build --wait --wait-timeout 120
docker compose ps
curl --fail http://localhost:8080/health/live
curl --fail http://localhost:8080/health/ready
docker compose down
```

Do not add `--volumes` to shutdown: retain the named development volume.
For native `dotnet run`, set `ConnectionStrings__Postgres` explicitly; .NET does
not automatically load `.env`. The runbook provides shell-specific examples.

## Health contract

| Endpoint | Meaning | HTTP/body |
| --- | --- | --- |
| GET `/health/live` | The process can answer; PostgreSQL is deliberately excluded | `200 Healthy`, including database/configuration failures |
| GET `/health/ready` | A safely configured PostgreSQL connection can execute `SELECT 1` | `200 Healthy` on success; `503 Unhealthy` on unavailable/invalid configuration |

Responses expose neither connection strings nor exception details. Probe
connection/query timeouts are two seconds each with a five-second health-check
deadline. Request cancellation propagates. Missing or malformed configuration and
the unchanged `CHANGE_ME_LOCAL_ONLY` placeholder do not make readiness healthy.
Configuration is captured when the probe is first created; changing it requires
host recreation. A database outage does not itself prevent process startup.

## Repository guidance

Read [AGENTS.md](AGENTS.md), [CONTRIBUTING.md](CONTRIBUTING.md), and
[SECURITY.md](SECURITY.md). Stage-0A documents retain their historical bootstrap
status paragraphs; their engineering rules remain active. This README and the
validation record describe the current implementation/evidence, not those older
status snapshots.

See [repository structure](docs/architecture/repository-structure.md),
[ADR 0001](docs/adr/0001-modular-monolith-bootstrap.md), and the
[scenario index](docs/scenarios/README.md). No deployment, release, package
publication, automatic staging, or Git commit is part of this stage.
