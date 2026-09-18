# ADR 0008: durable audit history and focused observability

## Decision

Record every meaningful durable Transfer state transition in PostgreSQL
`transfer_audit_events` in the same transaction as the state mutation. Keep
audit history append-oriented and bounded: previous state, new state, reason,
source, safe provider reference, transition version, and timestamp. Do not put
raw callback payloads, credentials, or full financial-style request bodies in
the audit table.

Expose focused OpenTelemetry traces and counters at API, provider, callback,
reconciliation, and outbox boundaries. The application uses BCL
`ActivitySource`/`Meter`; the API registers ASP.NET Core and HttpClient
instrumentation. No exporter or proprietary APM is required for deterministic
tests.

## Rationale

Logs are not durable business history, and inbox/outbox rows answer different
questions. A bounded audit record answers what happened to a Transfer locally
and why. PostgreSQL remains the authority. Traces and metrics make ambiguity,
callback backlog, and outbox redelivery diagnosable without logging secrets or
raw payloads.

## Dependency decision

**Problem:** ASP.NET Core/HttpClient spans and standard OpenTelemetry provider
registration should interoperate with common collectors without a proprietary
SDK.

**Why BCL is insufficient:** `ActivitySource` and `Meter` create signals but do
not register ASP.NET Core/HttpClient instrumentation or an OpenTelemetry
provider pipeline.

**Why existing dependencies are insufficient:** EF Core, Npgsql, and the web
framework do not provide that OpenTelemetry provider registration.

**Operational cost:** three centrally versioned packages and their transitive
OpenTelemetry runtime; no exporter, collector, or additional service is required.

**Failure modes introduced:** misconfigured export could lose telemetry or add
overhead, but cannot change financial-style state. Correctness never depends on
an exporter.

**Removal cost:** remove API registration/package references; BCL custom
instrumentation remains inert without listeners.

## Rejected alternatives

- Event sourcing: unnecessary for this failure laboratory and would replace the
  explicit state model.
- A process-local audit list: lost on restart and invalid across replicas.
- Raw payload logging: leaks data and does not provide durable state history.
- Exactly-once telemetry or distributed delivery claims: impossible across the
  database/HTTP boundary without a distributed transaction.

## Evidence boundary

Unit and contract instrumentation tests run locally. PostgreSQL audit
transaction tests and full container telemetry remain blocked when Docker is
unavailable. This ADR does not claim production readiness or compliance.
