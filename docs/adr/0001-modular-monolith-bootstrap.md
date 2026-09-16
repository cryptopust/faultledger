# ADR 0001: Modular-monolith bootstrap

Status: accepted for Prompt 0B. Scope: repository/tooling and health checks only.

## Context

FaultLedger needs a runnable technical foundation before its first business model.
[AGENTS.md](../../AGENTS.md) requires focused scope, simple boundaries, real
PostgreSQL evidence, deterministic tests, minimal dependencies, and truthful claims.
No current requirement calls for distributed deployment or message infrastructure.

## Decision

Start with one deployable ASP.NET Core application. Domain has no project or package
dependencies. Application references Domain; Infrastructure references Application
and Domain; Api references Application and Infrastructure at its composition root.
Domain and Application intentionally contain no business classes yet.

Use the CLI-generated `FaultLedger.slnx`: the installed .NET 10 SDK supports it and
Rider 2026.2.1 is installed. CLI validation will provide solution-loading evidence;
interactive Rider opening is not implied by that evidence.

Expose only GET liveness and readiness. Liveness excludes dependency probes.
Readiness performs a bounded authenticated PostgreSQL `SELECT 1` through
Infrastructure and the framework's `IHealthCheck`, without a new application
interface, ORM, repository, or schema. Database unavailability must not prevent
the process from starting. Missing/invalid configuration fails readiness closed.

Compose defines PostgreSQL and the API, localhost-only published ports, a named
database volume, and a healthy-database startup dependency. Pin PostgreSQL 18.6
and .NET container images by digest. The API uses a non-root Alpine runtime with
its existing `wget` for a readiness health check, rather than installing curl.
The development database role is the image's bootstrap administrator, not a
production least-privilege design. No business schema or migration is introduced.

Use one real-PostgreSQL test path: Testcontainers plus WebApplicationFactory.
Do not auto-skip missing Docker or substitute a fake database. Separate tests
exercise invalid configuration and an exclusively reserved, non-listening local
TCP endpoint without Docker; they prove negative health semantics, not successful
PostgreSQL connectivity. No sleeps, probabilistic failures, or business harness.

Keep the existing SDK 10.0.203 pin with `rollForward: disable`. Prompt 0B prefers
roll-forward, but the current governance verifier requires an exact pin. The
stricter rule wins; neither the verifier nor policy is weakened. Microsoft test
hosting and the runtime image use 10.0.7 to match the installed ASP.NET runtime.
Security/toolchain servicing remains an explicit follow-up decision, not an
automatic claim that pinned versions remain current indefinitely.

## Dependency decisions

There were no existing package dependencies. Versions are centralized; no project
version overrides are allowed. All additions serve this stage, not future features.

| Dependency | Problem / why BCL or framework and existing dependencies are insufficient | Operational cost / failure modes introduced | Removal cost |
| --- | --- | --- | --- |
| Npgsql 10.0.3 | Required PostgreSQL wire protocol and authenticated query; .NET does not ship a PostgreSQL driver | One runtime driver and its dependencies; connection, protocol, authentication and pool failures; bounded checks and safe logs required | Replace the Infrastructure health check's data access |
| Microsoft.AspNetCore.Mvc.Testing 10.0.7 | Exercise the actual application startup, routing, DI and HTTP health boundary with WebApplicationFactory; framework runtime alone has no test factory | Test-only host/content-root behavior; keep configuration isolated | Replace host factory and HTTP tests |
| Testcontainers.PostgreSql 4.15.0 | Isolated real PostgreSQL lifecycle and readiness without a hand-built Docker harness | Test-only Docker/image/network prerequisite and cleanup; missing engine is a failure, not a skip | Replace container fixture, not production code |
| Microsoft.NET.Test.Sdk 18.10.1 | VSTest discovery/execution for root `dotnet test` and IDE tooling | Test-only runner/testhost compatibility | Replace test execution integration |
| xunit.v3 3.2.2 | Assertions, theories, and async fixture lifecycle; no testing framework existed | Test-only framework/analyzers; strict warnings remain active | Rewrite tests if framework changes |
| xunit.runner.visualstudio 3.1.5 | Bridge xUnit v3 to VSTest/IDE discovery; test framework alone is not that adapter | Test-only discovery compatibility | Replace adapter with runner migration |

The repository maintainer owns updates and removal decisions. Restore/audit, build,
test discovery, deterministic regressions, and format verification validate these
choices. Package licensing and transitive dependencies are reviewed with the
resolved inventory. Infrastructure references the ASP.NET Core shared framework
for health/DI/configuration abstractions; Domain and Application do not.

## Consequences

One host and one durable infrastructure dependency keep failure boundaries visible.
Tests inspect evaluated project dependencies and compiled assembly references;
this does not prove every possible future semantic Domain-purity violation.
CI runs the same local commands and requires Docker for the PostgreSQL tests.
No deployment, publishing, schema evolution, financial guarantees, or production
readiness is claimed. Docker execution is initially blocked on this workstation
because Docker and WSL are absent; actual validation results belong in the
[bootstrap validation record](../runbooks/bootstrap-validation.md).

## Alternatives considered

Microservices, Kafka/RabbitMQ and other brokers introduce operational and failure
cost without a current need. Redis, MediatR, Serilog, EF Core, repository/CQRS
frameworks, OpenTelemetry, coverage collectors, and architecture packages are not
needed for two health endpoints and dependency checks. Manual database processes
would create a second test infrastructure path. A fabricated readiness result
would remove the very boundary these tests must exercise.

## Primary references

- [ASP.NET Core health checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [ASP.NET Core integration tests](https://learn.microsoft.com/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
- [Npgsql data sources](https://www.npgsql.org/doc/basic-usage.html)
- [Testcontainers PostgreSQL](https://dotnet.testcontainers.org/modules/postgres/)
- [xUnit v3 test execution](https://xunit.net/docs/getting-started/v3/getting-started)
- [PostgreSQL 18.6 release](https://www.postgresql.org/docs/release/18.6/)
- [PostgreSQL image and volume layout](https://hub.docker.com/_/postgres)
- [Compose startup dependencies](https://docs.docker.com/compose/how-tos/startup-order/)
- [Rider SLNX support](https://blog.jetbrains.com/dotnet/2024/10/04/support-for-slnx-solution-files/)
