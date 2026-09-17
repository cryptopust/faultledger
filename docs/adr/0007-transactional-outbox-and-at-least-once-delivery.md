# ADR 0007: Transactional outbox and at-least-once delivery

## Status

Accepted for Stage 6.

## Context

A local Transfer completion can commit while an outbound integration event is
not yet published. Conversely, a consumer can commit an effect while the
publisher crashes before recording local publication. The delivery boundary
therefore cannot provide a distributed exactly-once guarantee.

## Decisions

* `TransferCompleted` is the first small, versioned integration event.
* Transfer completion and its outbox row are inserted in one PostgreSQL
  transaction.
* Callback completion additionally commits the inbox terminal marker in that
  same transaction; reconciliation uses the same completion invariant.
* PostgreSQL uniquely identifies one logical completion per aggregate and event
  type with `uq_outbox_aggregate_event`.
* A dispatcher claims rows with `FOR UPDATE SKIP LOCKED`, a persisted lease,
  and a per-claim token. Network delivery happens after the claim transaction
  commits.
* Publication is at-least-once. The event ID is immutable across retries.
* The simulated consumer stores receipts and deduplicates logical effects by
  event ID in PostgreSQL.
* Toxiproxy exercises actual outbound HTTP path failures; it is not the
  provider semantic-failure mechanism.

## Alternatives rejected

Direct publish after saving the Transfer, in-memory queues/deduplication,
exactly-once claims, a database transaction held across HTTP, Redis locks, a
message broker, and generating a new event ID per retry were rejected. Each
either creates a message-loss window, relies on process-local state, or hides
the failure semantics this laboratory is intended to expose.

## Consequences

The outbox survives process restart and permits safe redelivery. A remote
consumer may observe duplicate deliveries, and the logical effect must remain
idempotent. Global event ordering is not promised. Retry scheduling is bounded
and deterministic. Broker delivery, transactional outbox publication to
multiple consumers, and Stage 7 hardening remain intentionally deferred.
