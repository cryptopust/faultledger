# Database rules

Authority: [AGENTS.md](../../AGENTS.md), especially FL-RULE-002, FL-RULE-004/005/006, FL-RULE-008, FL-RULE-015, and FL-RULE-018. Related: [FAILURE_MODEL.md](FAILURE_MODEL.md) and [TESTING_RULES.md](TESTING_RULES.md).

This document defines future persistence obligations. No schema, migration, database service, or persistence implementation is created by this bootstrap.

## Authority and schema evolution

PostgreSQL is the durable source of truth for operation existence, state, idempotency, inbox, outbox, and append-only audit history. A cache, Redis lock, singleton, application log, or provider response retained only in memory cannot replace it. A cache failure must not change correctness.

All real schema changes require versioned migrations. Do not rely on `EnsureCreated`, manual database edits, or startup SQL hacks. Persistence integration tests use actual migrations against real PostgreSQL, preferably via Testcontainers; do not prove a different schema with hand-built test tables or mocked EF `DbContext`.

Review migrations for data preservation, constraints, contention/locking, deploy/recovery implications, and compatibility with existing durable records. Do not promise reversible rollback for destructive operations; document forward repair when appropriate. No migration is required until a schema change exists.

## Constraints and indexes

Each unique constraint represents a named, documented invariant with an explicit key scope, including any tenant/caller scope introduced later. Application prechecks may improve responses but never replace constraint enforcement. Define null behavior and canonical identity before assuming uniqueness semantics match the business rule.

Each important index needs a correctness or access-path reason. Record the supported query/constraint and costs when meaningful. Do not add indexes for decoration or imagined scale. Do not remove a correctness index just because a benchmark appears faster without it.

## Durable idempotency and fingerprints

An idempotency key maps to one immutable canonical request (FL-RULE-006). Enforce uniqueness in PostgreSQL. Same key/same canonical request recovers the existing operation; same key/different canonical request is an explicit conflict. Never overwrite the stored fingerprint/request to make a conflict disappear.

Handle racing inserts and unique violations deliberately. `SELECT` then `INSERT` alone is insufficient. A failed write may abort its transaction; recovery must use a valid documented transaction path rather than continuing blindly. Test winners and losers under actual contention and inspect provider effects as well as rows.

Specify canonical serialization, invariant numeric/currency handling, field ordering, encoding, and fingerprint version. Use a cryptographic hash such as SHA-256 where appropriate. Include stable business fields; exclude volatile timestamps, generated IDs, trace IDs, attempt counters, server correlation IDs, and provider responses. Existing keys retain their original interpretation when canonicalization evolves; do not recalculate history silently under a new algorithm.

Document retention before expiring keys or inbox deduplication records. Deleting durable identity must not silently permit a duplicate side effect after a late retry. Retention/replay horizons are unresolved until explicitly designed and tested; this bootstrap does not invent one.

## Concurrency and ownership

Choose the smallest PostgreSQL mechanism that enforces the invariant: unique constraint, atomic conditional `UPDATE`, optimistic concurrency/version predicate, transaction, row lock, or explicit lease. A read followed by an unconditional write is not compare-and-swap. Check affected-row counts and distinguish conflicts from successful transitions.

`FOR UPDATE` is appropriate only with deliberate transaction lifetime and lock ordering. `SKIP LOCKED` can support work claiming; it does not guarantee ordering, fairness, or permission to repeat external submission. Test reclaiming work and stale ownership, not just selecting one row.

Lease claims need database-backed identity, expiry policy, ownership/version checks, and rejection of stale local writes. Clock assumptions must be documented and testable. A lease expiring cannot revoke an already in-flight remote call. Neither lease expiry nor fencing a local write proves the external operation did not happen (FL-RULE-003).

Local `SemaphoreSlim`, `lock`, mutexes, dictionaries, static fields, or singleton workers may help local efficiency, but removing them or starting another process must not break correctness. No homemade distributed locking service and no Redis lock as the primary financial correctness boundary. A proposed departure requires explicit rule redesign, not merely a dependency decision.

## Transaction-boundary record

For every important transaction, document:

```text
Invariant and participating durable records:
Preconditions / isolation / conflicts:
What becomes durable on commit:
What can crash or be cancelled next:
What the caller/worker can truthfully observe:
Recovery path after ambiguous commit acknowledgement:
Replay and stale-owner behavior:
```

Keep transactions short. Do not casually span arbitrary remote I/O or hold PostgreSQL locks while awaiting a provider. Any unavoidable exception needs a demonstrated requirement, written justification, bounded behavior, and relevant tests. Neither a DB transaction nor an HTTP retry policy makes a PostgreSQL/provider interaction atomic.

If commit acknowledgement is lost, the transaction may have committed. Recover by stable durable identity on a fresh valid connection when available; do not assume rollback or repeat an external side effect. Retrying a purely local transaction is allowed only when its replay cannot duplicate external work and its idempotency/conflict semantics are established.

## Dispatch boundary and no repost

Before a possible external side effect, record sufficient durable identity and dispatch intent for recovery to recognize a possibly attempted submission. Do not send while persistence needed for that intent is unavailable. The precise schema belongs to a later implementation task.

A crash after recording intent but before actual send may leave conservative uncertainty. That is safer than guessing and posting again. Recovery may resume submission only with proof that no prior dispatch occurred or remains in flight. Local intent, local rollback, absent response, provider `NotFound`, and lease expiry alone are not that proof.

If a provider accepts and the following DB write fails, the existing durable dispatch record must support conservative recovery. Do not claim `UNKNOWN` was persisted if the database rejected that write; report the persistence failure and recover the pre-existing ambiguous intent. Prefer safety over falsely improving availability by resubmitting.

## Inbox boundary

Authenticate and minimally validate the envelope, persist receipt under a durable deduplication identity, commit, then acknowledge. If persistence is unavailable or commit is uncertain, do not acknowledge successful durable receipt without proof. A retry may recover an already committed inbox record.

Separate recoverable business processing from receipt. The business effect and durable processing completion must commit atomically, or use an explicitly documented equivalent that proves replay safety. A duplicate receipt must not cause a duplicate effect. The same event identity with conflicting authenticated content is a conflict requiring observable handling, not permission to overwrite evidence.

## Outbox boundary

Local state change and its required outbox record commit together. Publishing happens through recoverable work outside arbitrary database-held remote locks. A publication can succeed before local acknowledgement is stored; publication status therefore cannot prove exactly-once delivery.

Consumers enforce duplicate tolerance durably. If a message causes provider submission, redelivery must recover the same dispatch identity and honor FL-RULE-003. An outbox alone does not make non-idempotent HTTP POST retries safe.

## State and audit

Use conditional state/version updates to enforce evidence-authorized transitions under concurrent callbacks, workers, and reconciliation. Terminal regression is prohibited except an explicitly modeled, demonstrated domain rule. A stale read must not authorize a stale write.

Important state transitions and their append-only audit evidence should be committed atomically when the audit behavior is implemented. Audit history is distinct from outbox messages, provider inbox records, and logs. Application roles and migration/maintenance privileges must preserve that distinction; calling a table "append-only" is not proof it is protected.

## Required evidence

Prove relevant invariants with real constraints, real migrations, actual contention, independent instances, and restart/crash scenarios. Tests inspect both durable rows and external simulator submission/effect history. See [TESTING_RULES.md](TESTING_RULES.md). Record exactly what was tested; no database behavior has been demonstrated in this governance stage.
