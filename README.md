# FaultLedger

A deterministic .NET engineering failure laboratory for studying distributed
financial-style orchestration. It is not a payment processor, bank, wallet,
real-money service, or compliance-certified platform. Synthetic data only.

## Current stage: deterministic provider failure laboratory (2)

The existing governance, nine-project .NET 10 solution, health endpoints,
PostgreSQL development Compose configuration, Dockerfile and CI are retained.
Stage 1's exact Money, guarded Transfer state machine, controlled timestamps,
explicit application orchestration, PostgreSQL EF mappings, migration, optimistic
version checks and transfer POST/GET contracts remain in place. Stage 2 adds a
deterministic MockProvider scenario catalog, an explicit provider acceptance
boundary, typed acceptance evidence, and a process-local simulated provider
ledger with separate submission-attempt and accepted-operation counters.

**Evidence boundary:** non-Docker behavior, compiled boundaries and migration
model/SQL generation can run locally. Real migration execution, PostgreSQL
round trips, concurrency and successful end-to-end submission require Docker.
Docker remains unavailable here; those tests fail prerequisite initialization,
not skip. Container builds/runtime and remote CI execution remain unverified.
See the [Stage 1 validation record](docs/runbooks/stage1-validation.md).
The [bootstrap record](docs/runbooks/bootstrap-validation.md) is historical.

**Not implemented:** canonical idempotency replay/fingerprints, automatic
Unknown detection, reconciliation, callbacks, inbox/outbox, audit history,
background workers, Redis, Toxiproxy or business telemetry. Stage 2's provider
ledger is fake external truth only; it is not PostgreSQL authority and does not
prove durable idempotency. No automatic retry or repost exists.
Unknown exists only as a domain state. Duplicate keys fail closed with 409; this
is not the completed idempotency contract.
Full Definition of Done is not satisfied while PostgreSQL evidence is blocked.

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
No test filter is used by CI or the full Definition of Done gate. For the
explicitly authorized conditional local checkpoint, run the non-Docker filter
three times as well; a missing Docker engine is not a passing database suite.

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
Apply the [explicit migration workflow](docs/runbooks/transfer-persistence.md)
before calling transfer endpoints. Startup never creates or migrates the schema.

## Transfer API (synthetic laboratory only)

`POST /api/transfers` accepts `clientReference`, `idempotencyKey`, `amount`, and
`currency`. For example:

```json
{"clientReference":"order-1001","idempotencyKey":"order-1001-attempt","amount":125.50,"currency":"USD"}
```

Amounts use exact fixed-point JSON numeric tokens with at most 20 integer and
8 fractional digits. Exponents, quoted numbers and excessive precision are
rejected rather than rounded. Currency validates three ASCII letters only,
not official currency status. References use ASCII letters, digits, `.`, `_`,
`:`, and `-`; client references allow 1-100 characters and keys 1-128.

Successful POST returns `201`, a Location, and an explicit response in `Accepted`
state, not `Completed`. `GET /api/transfers/{id}` reads local durable state;
missing identities produce a typed 404. Validation returns 400, duplicate keys
and concurrency conflicts 409, and classified persistence outages 503. No entity
or stack trace is returned. Keys are globally scoped and case-sensitive.

Do not automatically repost or replace a key after an error. A crash after
durable Submitting may leave a possibly accepted operation without local
certainty. No recovery/resubmission worker exists. This unauthenticated local
laboratory must not be exposed to an untrusted network or real payment data.

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
Readiness checks connectivity, not whether migrations have been applied or
whether a transfer outcome is known. Operators must apply migrations explicitly.

## Repository guidance

Read [AGENTS.md](AGENTS.md), [CONTRIBUTING.md](CONTRIBUTING.md), and
[SECURITY.md](SECURITY.md). Stage-0A documents retain their historical bootstrap
status paragraphs; their engineering rules remain active. This README and the
validation record describe the current implementation/evidence, not those older
status snapshots.

See [repository structure](docs/architecture/repository-structure.md),
[transfer design](docs/architecture/transfer-domain.md),
[ADR 0002](docs/adr/0002-transfer-domain-and-persistence.md),
[ADR 0003](docs/adr/0003-deterministic-provider-failure-model.md), and the
[scenario index](docs/scenarios/README.md). Prompt 1 authorizes reviewed local
checkpoint commits only; no push, release, tag or remote mutation is authorized.
