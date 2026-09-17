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
FaultLedger.SimulatedConsumer -> no FaultLedger project references; lab-only external consumer fixture
```

All projects inherit `net10.0`, nullable references, implicit usings, strict
warnings/analyzers, code style, and deterministic compilation from root props.
Versions live in `Directory.Packages.props`, with per-project dependency lock
files. Test defaults explicitly import the root props rather than replacing them.

| Project | Current responsibility |
| --- | --- |
| Domain | Immutable Money, validated Transfer identity/references, explicit state transitions and controlled timestamps |
| Application | Transfer creation/submission orchestration, transfer-specific persistence/provider contracts and read model |
| Infrastructure | PostgreSQL readiness, EF persistence records/mappings/migration, optimistic store, deterministic MockProvider scenarios/ledger, transactional outbox dispatcher and HTTP publisher |
| Api | Composition root, liveness/readiness, explicit POST/GET transfer DTOs and sanitized errors |
| SimulatedConsumer | Docker/in-process lab fixture that durably records integration-event receipts and deduplicates logical effects in PostgreSQL; not a second business system |
| Architecture.Tests | Evaluate actual MSBuild project/package/framework references and strict properties |
| Domain.Tests | Compiled boundary checks, exact money, invalid input, all state edges, terminal protection and timestamps |
| Application.Tests | Compiled boundary checks, submission ordering, validation, cancellation and storage-failure behavior using explicit unit doubles |
| IntegrationTests | Real HTTP contract/health tests, offline EF model checks, plus mandatory PostgreSQL migration/round-trip/concurrency/API/reload tests |

Domain cannot depend on configuration, logging, HTTP, ORM, database drivers, or
other infrastructure. Infrastructure uses the framework's existing health-check
interface; no speculative `IDatabaseReadinessProbe`, repository, or unit of work
is needed. Api contains no Npgsql queries or business logic. The simulated
consumer is intentionally outside the FaultLedger business graph and exists
only to expose at-least-once delivery and durable consumer deduplication.

## Persistence and provider placement

Infrastructure now owns EF, Npgsql and migration tooling. Domain objects are
rehydrated through validated construction rather than ORM mutable public setters.
Application sees ITransferStore and ITransferProvider, not EF/provider library
types. The API composes implementations but does not execute SQL or orchestrate
state transitions. The MockProvider ledger is a process-local fake external
model, not a FaultLedger correctness store. No generic repository, UnitOfWork or
real provider network client is required. See [transfer design](transfer-domain.md),
[ADR 0002](../adr/0002-transfer-domain-and-persistence.md) and [ADR 0003](../adr/0003-deterministic-provider-failure-model.md).

## Current operational boundary

The Dockerfile defines one intended API deployment image; its build and runtime
remain unverified on this workstation. Compose defines development PostgreSQL
and a named volume. Published ports are configured for loopback only. The
container dependency controls initial startup order, while application readiness
checks live connectivity independently. Liveness never waits on PostgreSQL.
Readiness says nothing about schema completeness, migrations, durable
business invariants, or financial safety.

Architecture checks evaluate the checked-in graph, not every possible future
conditional build or semantic I/O use hidden inside BCL types. Review remains
mandatory. Read [validation evidence](../runbooks/stage1-validation.md) before
interpreting a configured component as successfully exercised.
