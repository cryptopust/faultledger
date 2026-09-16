# Architecture rules

Authority: [AGENTS.md](../../AGENTS.md), particularly FL-RULE-001, FL-RULE-003, FL-RULE-013, and FL-RULE-017. Principles: [CONSTITUTION.md](CONSTITUTION.md). These are future implementation constraints; no projects or runtime components exist yet.

## Default shape and dependency direction

Use a simple modular monolith: one deployable application with explicit internal boundaries and PostgreSQL as durable authority. Separate responsibilities only when a current task needs them. Do not create placeholder layers, interfaces for every class, multiple deployables, or projects merely to satisfy a diagram.

When requested later, dependency direction is inward:

```text
Domain: appropriate BCL primitives and pure domain abstractions only
Application: coordinates use cases, depends on Domain and boundary contracts
Infrastructure: implements those contracts, may depend on Application/Domain
Host: composes implementations and presents explicit public contracts
```

The host may reference Infrastructure at the composition root for registration. Domain and Application must not depend on concrete Infrastructure; Application must not need provider/database-specific implementations to express a use case. Keep dependency direction mechanically testable when the relevant projects exist, without installing architecture-test packages in advance.

## Domain purity

Domain must not depend on ASP.NET Core, Entity Framework Core, Npgsql, Redis, HTTP clients/contracts, Docker, OpenTelemetry SDK, filesystem access, environment variables, configuration frameworks, or logging frameworks. A BCL namespace containing I/O does not make that dependency pure. Domain abstractions themselves must not leak database handles, EF types, HTTP responses, telemetry SDK types, or infrastructure lifecycle concerns.

Infrastructure adapts to domain requirements. Do not reshape the domain around ORM conventions or provider payloads. Domain state changes are explicit operations with validated evidence, not unrestricted setters or arbitrary strings (FL-RULE-011).

## Exact values, time, and identity

FL-RULE-001 prohibits `float` and `double` for exact financial values, including rates. Every monetary value has explicit currency. Define supported currency identifiers, allowed precision, numeric range, equality, canonicalization, overflow behavior, and input rejection before claiming correctness. Do not assume two decimal places, infer currency from deployment, use culture-sensitive defaults, or silently round.

Use controlled time through `TimeProvider` at application/infrastructure boundaries, or explicit time values/pure abstractions in domain operations (FL-RULE-012). Do not scatter system clock reads or use elapsed wall time as deterministic evidence. Failure scenarios and identifiers that influence them are explicit, reproducible inputs (FL-RULE-007).

## Restricted architecture

Prohibited by default unless a concrete current requirement proves necessity and the decision is reviewed:

```text
Kafka
RabbitMQ
MassTransit
NServiceBus
Kubernetes
service mesh
Dapr
Orleans
Akka.NET
MediatR
generic repository frameworks
event-sourcing frameworks
CQRS frameworks
microservice decomposition
GraphQL
gRPC
NoSQL databases
ElasticSearch
```

Distributed caches as system of record and custom distributed lock services are also prohibited. Unlike a dependency choice, changing durable authority or correctness boundaries requires an explicit amendment to FL-RULE-002/008 with documented proof. Do not smuggle an architectural change through a transitive dependency or an innocuous helper package.

Do not add infrastructure to "prepare for scale". A transactional outbox can be designed without a message broker. Do not replace functioning architecture because a different pattern seems fashionable.

## Dependency introduction policy

Preference: **BCL -> .NET/ASP.NET Core framework -> existing dependency -> justified new dependency**.

For each major new dependency, write the following in a scoped engineering decision before adding it:

```text
Problem:
Why BCL/framework capability is insufficient:
Why existing dependencies are insufficient:
Operational cost:
Failure modes introduced:
Removal cost:
```

Identify the concrete current requirement, alternatives, version, licensing/security implications, ownership, and validation. The user or responsible reviewer must authorize an exception to the restricted list. The decision cannot waive a non-negotiable rule. Unapproved speculative dependencies are not permitted. Minor additions still require a task-specific reason.

Centralize package versions in `Directory.Packages.props`. Do not put `Version`/`VersionOverride` on project package references, disable central management locally, or add nested version policies without an explicit policy change. Preview packages need explicit justification. Do not upgrade unrelated dependencies during a feature task. There are no declared packages in this bootstrap.

The installed stable SDK is pinned in `global.json` to make SDK selection and `AnalysisLevel=latest` reviewable. This does not pin future container images, packages, operating systems, or application runtime deployment; those decisions require evidence when relevant components exist.

## External-boundary policy

Future provider behavior is simulated with synthetic data, not connected to real payment rails. Separate transport outcomes from operation outcomes: rejection, unavailable transport, cancellation, and ambiguous acceptance are not interchangeable. FL-RULE-003 applies to every retry layer, including SDK/HTTP handler defaults. Disable unsafe automatic submission retries when implementing such a boundary; provider idempotency does not by itself waive the no-repost rule.

Use explicit public request/response contracts, never expose database/domain entities directly, never leak stack traces, and preserve useful error categories. Canonical logical identity uses stable, versioned business fields, not transport metadata (FL-RULE-006).

Use async I/O, propagate cancellation, observe worker failures, and define background lifetimes. A cancelled request after possible dispatch still needs durable ambiguity and recovery. Standard .NET configuration belongs outside Domain; configuration must not silently enable real provider access.

## Persistence, cache, and operational boundaries

PostgreSQL owns operation existence, state, idempotency, inbox, outbox, and audit truth (FL-RULE-002). Redis can be proposed only for a current, clearly justified ephemeral concern such as a dispensable cache. Cache loss or eviction must not compromise a durable invariant. No Redis lock substitutes for designing a PostgreSQL invariant (FL-RULE-008).

Avoid holding database transactions/locks across remote I/O. Record dispatch intent and uncertainty at deliberate durable boundaries, and recover according to [DATABASE_RULES.md](DATABASE_RULES.md) and [FAILURE_MODEL.md](FAILURE_MODEL.md), not a process-local promise.

Observability stays outside Domain: structured redacted logs, useful traces/metrics, and a separate durable append-only audit trail for important state changes. Neither telemetry nor outbox delivery is the audit authority. Do not add operational tooling before the component it supports exists.
