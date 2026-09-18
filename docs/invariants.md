# FaultLedger invariant catalog

This catalog is the claim-to-proof index for the failure laboratory. `PASS`
means the relevant test ran successfully in the current environment. `BLOCKED BY
ENVIRONMENT` means an implemented PostgreSQL/Compose test could not run because
the Docker engine is unavailable; it is not a passing result.

| ID | Invariant | Enforcement | Primary evidence | Status |
| --- | --- | --- | --- | --- |
| FL-INV-001 | Money never uses binary floating point. | `Money` uses validated `decimal`; domain boundary tests reject unsupported precision. | `MoneyTests` | PASS |
| FL-INV-002 | One idempotency key identifies one immutable logical request. | SHA-256 fingerprint plus PostgreSQL unique key and deterministic conflict handling. | `TransferServiceTests`; PostgreSQL contention tests | PASS for local tests; Docker contention BLOCKED |
| FL-INV-003 | One logical transfer cannot submit twice through replay or ordinary concurrency. | Durable registration and `ReadyToSubmit -> Submitting` conditional claim. | `TransferServiceTests`; `SameIdempotencyKey_OneHundredConcurrentRequests_SubmitsProviderExactlyOnce` | Docker proof BLOCKED |
| FL-INV-004 | Ambiguous acceptance never authorizes automatic repost. | `Unknown`, `DoNotRepost`, lookup-only reconciliation, and no `SubmitAsync` call from recovery paths. | `UnknownOutcomeLaboratoryTests`; reconciliation tests | PASS |
| FL-INV-005 | Provider `NotFound` does not prove safe absence. | Reconciliation preserves `Unknown`. | `NotFound` reconciliation tests | PASS |
| FL-INV-006 | Duplicate callbacks do not duplicate business effects. | Unique provider event ID plus durable inbox receipt. | callback contract tests; PostgreSQL duplicate tests | Contract PASS; PostgreSQL BLOCKED |
| FL-INV-007 | Stale callback evidence cannot regress stronger state. | Explicit domain transition graph and callback state machine. | `CallbackTransitionTests`; out-of-order callback tests | PASS; PostgreSQL BLOCKED |
| FL-INV-008 | Inbox receipt survives processing failure. | Receipt commits before acknowledgement; processing is a separate recoverable transaction. | callback processing rollback tests | PostgreSQL BLOCKED |
| FL-INV-009 | Business state and required outbox intent commit atomically. | EF transaction in transfer update and callback processor; migration-backed outbox. | outbox atomicity/rollback tests | PostgreSQL BLOCKED |
| FL-INV-010 | Outbox redelivery retains the same event ID. | Event ID is stored as the outbox primary key and reused by every attempt. | crash-after-publish test | PostgreSQL BLOCKED |
| FL-INV-011 | Consumer logical effects are duplicate-safe. | Simulated consumer PostgreSQL primary key and immutable-message check. | duplicate delivery test | PostgreSQL BLOCKED |
| FL-INV-012 | Restart preserves durable transfer, inbox, outbox, and audit truth. | PostgreSQL is the authority; no process-local correctness state. | restart/container tests | PostgreSQL BLOCKED |
| FL-INV-013 | Important Transfer transitions have append-only audit evidence. | `transfer_audit_events` is inserted in the same transaction as the transition. | outbox audit assertions and callback/reconciliation tests | PostgreSQL BLOCKED |
| FL-INV-014 | Callback bodies are bounded before authentication/parsing work. | Streaming read stops at 2049 bytes, independent of `Content-Length`. | `OversizedChunkedBody_IsRejectedWithoutReadingAnUnboundedPayload` | PASS |

The audit table records local state history, not raw callback payloads or a
second event-sourcing model. It is distinct from the provider inbox, outbox, and
logs. Docker-dependent rows remain explicitly unproven on this workstation.
