# FaultLedger

FaultLedger is a deterministic .NET laboratory for studying failure modes in
distributed financial-style transaction orchestration.

FaultLedger is not a real payment processor and does not move real money. It is
not a bank, wallet, card processor, PCI-certified platform, or production-ready
financial system. Use synthetic data and local-only credentials.

## Engineering goals

The repository makes failure boundaries visible: exact money, explicit transfer
states, durable idempotency, no blind repost after ambiguous transmission,
recoverable callback receipt, transactional outbox delivery, duplicate-safe
consumption, and honest evidence classification. Correctness lives in explicit
domain rules and PostgreSQL invariants, never a process-local lock or cache.

## Architecture

FaultLedger is a small modular monolith: API -> Application -> Domain, with
Infrastructure adapting PostgreSQL, HTTP, and synthetic provider boundaries.
PostgreSQL owns transfers, fingerprints, inbox, outbox, and audit history. The
outbox dispatcher publishes through Toxiproxy to a small simulated consumer.
There is no Redis, broker, CQRS/event-sourcing framework, or real provider rail.
See the [real topology](docs/architecture/faultledger-topology.md) and
[repository graph](docs/architecture/repository-structure.md).

## Repository structure

- `src/FaultLedger.Domain`: exact `Money` and guarded `Transfer` transitions.
- `src/FaultLedger.Application`: orchestration, contracts, diagnostics.
- `src/FaultLedger.Infrastructure`: EF/PostgreSQL, provider lab, inbox/outbox.
- `src/FaultLedger.Api`: Minimal API, health checks, telemetry composition.
- `src/FaultLedger.SimulatedConsumer`: durable duplicate-safe lab consumer.
- `tests`: domain, application, infrastructure, architecture, and real
  PostgreSQL/Testcontainers integration evidence.

## Running locally

Install the SDK pinned by `global.json`. Docker with Linux containers is needed
for the full suite and local stack. Copy `.env.example` to an untracked `.env`
and replace the explicit placeholder with a synthetic local password. Then:

```text
docker compose config --quiet
docker compose up -d --build --wait --wait-timeout 120
docker compose ps
docker compose down
```

Do not add `--volumes` to routine shutdown. The [local runbook](docs/runbooks/local-development.md)
contains native-host and migration instructions.

## Running tests

```text
powershell -NoProfile -File scripts/Verify-Governance.ps1
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --no-restore
dotnet test --no-build --no-restore --filter 'Category!=RequiresDocker'
dotnet format --verify-no-changes --no-restore
```

The full suite intentionally fails rather than silently skipping when Docker is
unavailable. The filtered suite is diagnostic only; it is not PostgreSQL proof.

## Money model

Money uses `decimal`, explicit three-letter uppercase currency, PostgreSQL
`numeric(28,8)`, invariant canonicalization, and rejection of unexpected
precision. JSON amounts must be fixed-point numeric tokens; quoted values,
exponents, excessive scale, zero, and negatives are rejected rather than rounded.

## Transfer lifecycle

The guarded lifecycle is `Created -> ReadyToSubmit -> Submitting`, then confirmed
`Accepted`/`Failed`, ambiguous `Unknown`, lookup/callback resolution to
`Accepted`/`Completed`/`Failed`, or `ManualReview`. Terminal or stronger evidence
cannot be overwritten by a stale callback. Arbitrary state assignment is not a
public domain operation.

## Idempotency and high-contention concurrency

One globally scoped, case-sensitive idempotency key maps to one immutable,
versioned SHA-256 request fingerprint. Same key/same fingerprint replays the
durable operation; same key/different fingerprint is a conflict. PostgreSQL
uniqueness and a durable `ReadyToSubmit -> Submitting` claim prevent ordinary
multi-host races. A replay never steals a stranded `ReadyToSubmit` claim; this
conservative no-repost behavior is a documented limitation.

## Provider failure laboratory

Named synthetic scenarios distinguish failure before acceptance from ambiguous
failure after possible acceptance. Submission attempt and accepted-operation
counters are separate observable facts. The provider is fake external truth,
not FaultLedger's idempotency mechanism. See [scenario details](docs/scenarios/mock-provider.md).

## Unknown outcomes, DoNotRepost, and reconciliation

A missing success response does not prove failure. Ambiguous dispatch becomes
`Unknown` with `DoNotRepost`. Reconciliation performs `LookupAsync` only and
distinguishes accepted, completed, rejected, still unknown, temporary failure,
and `NotFound`. `NotFound`, elapsed time, lease expiry, cancellation, or restart
does not authorize a new `SubmitAsync` call.

## Durable callback inbox

`POST /api/provider-callbacks` requires a synthetic HMAC signature, rejects
unknown JSON members, and bounds the streamed body at 2048 bytes before buffering
the full envelope. Valid receipt is committed before HTTP acknowledgement.
Provider event ID uniqueness makes repeated/concurrent delivery safe. Processing
is recoverable and commits Transfer mutation, inbox status, audit event, and any
required outbox event together. The host currently exposes the processing service
but does not register an automatic inbox worker; tests drive processing explicitly.

## Duplicate and out-of-order callbacks

Duplicates return the original inbox identity and produce no second logical
effect. `Completed -> Accepted` and completed/accepted followed by rejection are
stale evidence, not state regressions. Callback/reconciliation contention relies
on PostgreSQL row/optimistic concurrency semantics.

## Transactional outbox and at-least-once delivery

`TransferCompleted` is created in the same PostgreSQL transaction as completion.
Dispatchers claim due work with `FOR UPDATE SKIP LOCKED`, persist a recoverable
lease and fencing token, release the transaction, then perform HTTP. Publication
is at least once: a remote effect may commit before local `published_at` does.
Redelivery uses the same event ID. No exactly-once distributed-delivery claim is
made. See [outbox architecture](docs/architecture/transactional-outbox.md).

## Consumer idempotency and crash/restart behavior

The simulated consumer durably keys receipts by event ID, rejects a reused ID
for a different immutable message, counts every receipt, and applies one logical
effect. Pending inbox/outbox/audit/transfer truth survives host restart because
PostgreSQL is authoritative. Process memory is never the durable correctness
boundary.

## Toxiproxy network failures

Compose routes `FaultLedger -> Toxiproxy -> simulated consumer`. Tests model
latency timeout, unavailable connection, and response-path loss after a durable
remote effect. A timeout is ambiguous; it cannot prove non-delivery. These tests
exist but were not executed on this Docker-unavailable workstation. See
[network scenarios](docs/scenarios/network-failures.md).

## Audit history

`transfer_audit_events` stores bounded, append-oriented local history: previous
and new state, source, reason category, safe provider reference, version, and
time. Audit rows commit with the state transition and are distinct from inbox,
outbox, and logs. Raw callback payloads and secrets are excluded. FaultLedger is
not event sourced.

## Observability

The API registers OpenTelemetry ASP.NET Core/HttpClient instrumentation and the
`FaultLedger` ActivitySource/Meter. Focused spans cover transfer creation,
provider submission, reconciliation, callback receive/process, and outbox
dispatch. Counters cover created transfers, provider submissions, Unknown,
reconciliation, idempotency replay/conflict, callback duplicates, and outbox
redelivery. No exporter is bundled. Metric labels exclude transfer IDs, payloads,
secrets, authorization values, connection strings, and money fields.
The [observability contract](docs/architecture/observability.md) maps each metric
to the operational question it answers.

## Health and public API

- `GET /health/live`: process liveness only; no external dependency.
- `GET /health/ready`: safely configured PostgreSQL responds to `SELECT 1`.
- `POST /api/transfers`: validated creation/idempotent replay.
- `GET /api/transfers/{id}`: durable local state.
- `POST /api/transfers/{id}/reconcile`: lookup-only ambiguity resolution.
- `POST /api/provider-callbacks`: bounded authenticated durable receipt.

Readiness excludes the simulated consumer/Toxiproxy and does not prove migrations
are current or a financial outcome is known. The API is an unauthenticated local
lab and must not be exposed to an untrusted network.

## Scenario catalog, core invariants, and traceability

The [scenario catalog](docs/scenarios/README.md), [invariant catalog](docs/invariants.md),
and [test matrix](docs/test-matrix.md) connect each claim to its enforcement and
test. Governance lives in [AGENTS.md](AGENTS.md) and `docs/engineering`.

## Evidence status

### Proven in the current environment

- Governance verifier, locked restore, build, non-Docker tests, format, and
  package vulnerability query.
- Exact Money/domain transitions, deterministic provider scenarios, no-repost
  orchestration, reconciliation semantics, callback HTTP contract/body bound,
  dispatcher logic, and instrumentation-boundary tests.

### Implemented but Docker-blocked

- Actual migration execution and PostgreSQL constraint/transaction evidence.
- 100-way idempotency contention and multi-instance/restart evidence.
- Durable inbox/outbox/audit/consumer dedupe and crash-after-publish scenarios.
- Live Compose and Toxiproxy latency/unavailable/response-loss scenarios.

### Documented design or limitation

- No global delivery ordering; fixed five-second outbox retry; no max attempts
  or dead-letter workflow; dispatcher lease is not renewed during slow publish.
- No automatic callback inbox worker in the application host.
- A crash leaving `ReadyToSubmit` or `Submitting` remains conservatively
  unresolved; elapsed time never authorizes automatic repost.
- Synthetic external systems, shared PostgreSQL in the Compose lab, no exporter,
  no real provider integration, no security/compliance certification.

## Non-goals

Kafka, RabbitMQ, Redis, Kubernetes, service mesh, event sourcing, a CQRS
framework, global ordering, automatic financial resubmission, a wallet/account
system, a real ledger, production deployment, and real financial data are out of
scope. FaultLedger finishes as a focused failure laboratory.
