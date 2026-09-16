# ADR 0005: UNKNOWN outcomes and safe reconciliation

## Context

Stage 3 durably claims `Submitting` before provider I/O and correctly suppresses
replay submissions, but a response can disappear after the provider accepts an
operation. Treating that transport symptom as failure or resetting the transfer
to `ReadyToSubmit` could create a duplicate external operation.

## Decisions

1. Ambiguous provider evidence (`TIMEOUT_AFTER_ACCEPT`, connection loss after
   acceptance, provider 500 after acceptance, malformed response after
   acceptance, and cancellation after dispatch authority) maps
   `Submitting -> Unknown`.
2. `Unknown` is non-final and exposes `RetryAdvice=DoNotRepost`. Same-key replay
   returns the durable transfer and never calls `SubmitAsync`.
3. Provider lookup is a separate `ITransferLookup` boundary. Its correlation
   key is the durable `TransferId`, which is sent before a response can be
   lost and survives host restart.
4. `ConfirmedAccepted`, `ConfirmedCompleted`, and `ConfirmedRejected` are the
   only lookup evidence that resolves `Unknown`. `NotFound`, `StillUnknown`,
   and lookup transport failure preserve uncertainty.
5. Reconciliation is an explicit `POST /api/transfers/{id}/reconcile` operation.
   It performs no submission and uses existing optimistic concurrency to handle
   concurrent resolvers.

## Consequences

The laboratory can preserve safety through ambiguous responses and later apply
provider evidence without reposting. A provider reference returned in a lost
response is not required for lookup. `Accepted` remains non-final until the
existing completion transition; `ConfirmedCompleted` may resolve directly from
`Unknown` with explicit provider evidence.

The fake provider ledger is intentionally process-local observation state. It
does not become FaultLedger authority and does not provide production durability.

## Alternatives rejected

- timeout or HTTP 500 mapped directly to `Failed`
- `Unknown -> ReadyToSubmit` or automatic repost
- treating provider `NotFound` as proof of absence
- lookup implemented by reusing `SubmitAsync`
- retry after an arbitrary age or a background scheduler
- callbacks, inbox/outbox, Redis, Toxiproxy, or a message broker in this stage

## Known limitations

A crash after durable submission claim and before local ambiguity classification
can leave `Submitting`; this stage does not invent dispatch certainty or reset
that state. Callback authentication, durable callback inbox, and recoverable
out-of-order callback processing remain future work. PostgreSQL/Testcontainers
proof is environment-dependent and is reported as blocked when Docker is absent.
