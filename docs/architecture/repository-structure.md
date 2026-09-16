# Repository structure

Authority: [AGENTS.md](../../AGENTS.md) and the
[architecture rules](../engineering/ARCHITECTURE_RULES.md). Decision record:
[ADR 0001](../adr/0001-modular-monolith-bootstrap.md).

## Production project graph

```text
FaultLedger.Domain          -> no projects, NuGet packages, or ASP.NET framework
FaultLedger.Application     -> Domain
FaultLedger.Infrastructure  -> Application, Domain
FaultLedger.Api             -> Application, Infrastructure
```

All projects inherit `net10.0`, nullable references, implicit usings, strict
warnings/analyzers, code style, and deterministic compilation from root props.
Versions live in `Directory.Packages.props`, with per-project dependency lock
files. Test defaults explicitly import the root props rather than replacing them.

| Project | Current responsibility |
| --- | --- |
| Domain | Empty pure-library boundary; no domain model or placeholder class |
| Application | Empty use-case coordination boundary with an inward Domain reference |
| Infrastructure | PostgreSQL readiness `IHealthCheck`, pooled Npgsql data source ownership, DI registration |
| Api | Application entry point/composition root and GET liveness/readiness mappings only |
| Architecture.Tests | Evaluate actual MSBuild project/package/framework references and strict properties |
| Domain.Tests | Verify compiled Domain assembly references do not acquire non-permitted dependencies |
| Application.Tests | Verify compiled Application assembly does not reference host or adapter libraries |
| IntegrationTests | Real application HTTP host, isolated configuration, negative health tests, and mandatory Testcontainers PostgreSQL tests |

Domain cannot depend on configuration, logging, HTTP, ORM, database drivers, or
other infrastructure. Infrastructure uses the framework's existing health-check
interface; no speculative `IDatabaseReadinessProbe`, repository, or unit of work
is needed. Api contains no Npgsql queries or business logic.

## Future work placement, not implemented components

When a later task requests persistence, its adapters/migrations belong under
Infrastructure, with real PostgreSQL evidence in IntegrationTests. Provider
adapters also belong at the Infrastructure boundary; Domain remains independent
of their payloads or SDKs. Domain and application behavior will receive tests in
their existing test projects. These statements allocate responsibility only;
none authorizes creating a schema, provider, event flow, or model now.

## Current operational boundary

The Dockerfile defines one intended API deployment image; its build and runtime
remain unverified on this workstation. Compose defines development PostgreSQL
and a named volume. Published ports are configured for loopback only. The
container dependency controls initial startup order, while application readiness
checks live connectivity independently. Liveness never waits on PostgreSQL.
Readiness says nothing about future schema completeness, migrations, durable
business invariants, or financial safety.

Architecture checks evaluate the checked-in graph, not every possible future
conditional build or semantic I/O use hidden inside BCL types. Review remains
mandatory. Read [validation evidence](../runbooks/bootstrap-validation.md) before
interpreting a configured component as successfully exercised.
