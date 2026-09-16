# ADR 0006: Durable callback inbox

## Context

Provider callback delivery is an untrusted at-least-once channel. HTTP requests can be duplicated, concurrent, delayed, out of order, or interrupted after receipt. Stage 4 already models an ambiguous submission as `Unknown` and forbids reposting.

## Decision

- Authenticate a synthetic callback before accepting it and validate only the bounded envelope needed for durable storage.
- Insert the callback into PostgreSQL `provider_inbox` before acknowledging receipt.
- Enforce named uniqueness on `provider_event_id`; a duplicate is an idempotent receipt, not a new event.
- Process inbox rows with a PostgreSQL row claim. Transfer mutation and the terminal inbox status share one transaction.
- Correlate only with the durable provider-operation correlation (the TransferId); never guess by amount or display reference.
- Allow callback evidence to resolve `Unknown` and advance valid `Submitting`/`Accepted` states, but never call provider submission.
- Treat stale and out-of-order evidence as retained, non-regressive processing. Poison or unknown-correlation events are retained as failed diagnostics instead of tight-loop retries.

## Alternatives rejected

- Processing business effects directly in the HTTP request.
- Process-local duplicate sets or locks.
- Assuming exactly-once or chronological delivery.
- Dropping stale/duplicate evidence.
- Introducing a broker, outbox, or background financial retry policy at this stage.

## Consequences and limitations

PostgreSQL is authoritative for callback receipt, deduplication, processing status, and transfer truth. The explicit processor endpoint makes tests deterministic; a production scheduler is intentionally deferred. The synthetic HMAC secret must be supplied through configuration, and Docker-blocked tests cannot currently prove PostgreSQL contention or restart behavior in this environment.
