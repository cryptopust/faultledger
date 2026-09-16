# Prompt 0B validation record

Recorded on 2026-09-16. This is evidence for the repository/tooling bootstrap,
not an implemented financial-system guarantee. Authority:
[AGENTS.md](../../AGENTS.md); current setup: [README](../../README.md).

## Environment and scope

Windows 10.0.26100, Windows PowerShell 5.1, .NET SDK 10.0.203, ASP.NET Core
runtime 10.0.7, Git 2.55.0.windows.5, and installed Rider 2026.2.1. Git remains
on `main` with no commits; the stage-0A baseline was already untracked when this
task began. No staging, commit, push, tag, or branch creation was performed.

Eight projects were generated using the real .NET CLI and added to
`FaultLedger.slnx`. No Money/Transfer model, provider, business schema, migration,
idempotency, callback, inbox/outbox, reconciliation, or audit implementation exists.
Domain and Application are deliberately empty libraries with enforced boundaries.

## Observed command results

| Check | Result and evidence boundary |
| --- | --- |
| `dotnet restore` | PASS: all eight projects restore |
| `dotnet restore --locked-mode` | PASS: generated dependency locks agree with declared versions |
| `dotnet build` | PASS: all eight projects; zero warnings and errors |
| `dotnet test --no-build --no-restore` | FAIL: 14 passed, 3 failed, 0 skipped; all three failures are the unavailable Docker prerequisite |
| `dotnet test --no-build --no-restore --filter 'Category!=RequiresDocker'` | PASS: 14 diagnostic checks; explicitly not the full suite or PostgreSQL evidence |
| `dotnet format --verify-no-changes` | PASS: no formatter changes required |
| `dotnet format --verify-no-changes --no-restore` | PASS: the CI-equivalent formatting command |
| `dotnet publish src/FaultLedger.Api/FaultLedger.Api.csproj --configuration Release --no-restore --output artifacts/publish /p:UseAppHost=false` | PASS: publish output created under ignored artifacts; not an image build |
| Standalone Docker Compose 5.5.1 `config --quiet` | PASS with a process-local synthetic password; same Compose implementation, without a Docker engine |
| `docker build .` | BLOCKED: Docker CLI is not installed/on PATH |
| Standalone Compose `up -d --build --wait --wait-timeout 30` | FAIL: Docker engine named pipe does not exist; no stack started |
| Standalone Compose `ps` | FAIL: same missing engine, not evidence of running services |
| Published Kestrel HTTP smoke check | PASS: Production host starts with no DB configuration; live returns `200 Healthy`, ready returns `503 Unhealthy`, both text/plain |
| `dotnet list FaultLedger.slnx package --vulnerable --include-transitive --no-restore` | No known vulnerable packages reported for the eight projects by the configured NuGet source; not a complete security audit |

The native host listened on a dynamically allocated loopback port and was stopped
with Ctrl+C; its shutdown log was observed. No named development volume was
created or removed. Full Docker-backed repeat-run stability is unverified, not a
passing claim. The closing task report records the final repeated full-suite runs.

## Docker blocker

`docker --version` / `docker compose version` fail because the command is absent.
The normal Docker Desktop installation paths and Docker service are absent, WSL
reports that it is not installed, and no remote Docker endpoint is configured.
Testcontainers reports `DockerUnavailableException` for
`npipe://./pipe/docker_engine`. No tests are auto-skipped, mocked, or weakened to
hide this failure.

The official standalone Compose binary was downloaded only to a temporary
directory for configuration validation, without installing a service or changing
PATH. Its published SHA-256 was verified:
`A3C0C73033EAEDE90210345D0CC2233EDF4FAB8FE0282A91DAD8FD8436809D2F`.
Image manifests/digests were checked at their registries; this does not prove that
the API image builds or runs. Docker Desktop/WSL installation and machine changes
were deliberately not performed as an implicit repository edit.

## Tests and review findings

Seventeen test cases are discoverable: four evaluated-project graph/default
checks, two compiled-assembly boundary checks, eight HTTP negative/method/surface
checks, and three real-PostgreSQL tests. The latter cover reachable health, absence
of application tables, and database stop/restart with independent host recreation.
They have not passed here because their real infrastructure prerequisite is absent.

Review fixed a nullable path handling error rather than suppressing its compiler
warning, removed all template classes/tests/weather functionality, and normalized
template source/configuration formatting. The restart test uses the container's
current mapped address and a fresh cleanup deadline rather than assuming port
reuse or reusing an already-cancelled recovery token. The refused-endpoint test
holds an exclusive non-listening socket instead of guessing an unused port.

No application package beyond Npgsql is present. Test-only transitive dependencies
include runner telemetry and code-coverage components; these are not application
instrumentation or a requested coverage collector. Direct package licenses were
inspected: PostgreSQL license for Npgsql, MIT for Microsoft/Testcontainers
packages, and Apache-2.0 for xUnit and its VSTest adapter. No coverage percentage,
formal compliance, or vulnerability-free guarantee is asserted.

## Policy conflict and limitations

Prompt 0B prefers SDK roll-forward. Existing governance and its verifier require
the exact SDK pin. The stricter setting remains: 10.0.203, roll-forward disabled,
prerelease selection disabled. No rule or checker was weakened. The three changed
shared configuration files are build props, central package props, and the example
environment file; their strict defaults remain active.

Governance documents retain their stage-0A status snapshots. Current implementation
status is maintained in README and this record without rewriting their rules.
There is no configured CI execution history or verified interactive Rider session.
CLI solution loading, build and test discovery do not imply those checks passed.
Architecture tests are useful baseline checks, not exhaustive proof against every
future conditional dependency or BCL-based I/O violation.

Full Definition of Done is **not satisfied** while Docker-dependent validation is
blocked. On a machine with a working Linux-container engine, run the complete
commands in the [local runbook](local-development.md), including two full test
runs, image build, Compose startup, both healthy HTTP responses, and shutdown
without deleting the named development volume. Preserve any failure as evidence;
do not replace the required PostgreSQL tests with the diagnostic filtered run.
