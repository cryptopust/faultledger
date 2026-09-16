# ADR 0004: Durable idempotency and submission claim

## Context

Stage 1 reserved idempotency keys but rejected every duplicate. That was safe
against reposting but did not provide same-request replay or distinguish a
changed request. It also left a second race: two callers could observe the same
`ReadyToSubmit` transfer and both invoke the provider.

## Decisions

1. PostgreSQL owns the global idempotency-key race with the named unique index
   `uq_transfers_idempotency_key`.
2. A versioned SHA-256 fingerprint of client reference, canonical decimal
   amount and uppercase currency defines the immutable logical request.
3. A conditional PostgreSQL update from `ReadyToSubmit/version=1` to
   `Submitting/version=2` is the durable external-submission claim.
4. The claim commits before provider I/O; no database transaction is held over
   the external call.
5. Same-key/same-fingerprint requests replay the stored transfer. A different
   fingerprint returns an explicit HTTP 409 conflict and never calls the
   provider.
6. Process-local locks/caches are not used for correctness. The MockProvider
   ledger remains a process-local laboratory observation boundary only.

## Consequences and known limitation

Concurrent callers, independent hosts, and restarts can recover one durable
logical operation without another provider invocation. A crash after the claim,
during provider I/O, or after provider acceptance but before local acceptance
persistence leaves `Submitting`. Stage 3 intentionally does not infer UNKNOWN,
reconcile, reset the claim, or retry; Stage 4 owns that recovery decision.

## Alternatives rejected

- `SELECT` then `INSERT` without a PostgreSQL uniqueness race handler
- `SemaphoreSlim`, `lock`, singleton caches, or Redis locks
- Holding a PostgreSQL transaction open during provider I/O
- Automatic provider retry after an ambiguous response
