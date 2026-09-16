# Durable idempotency and submission claim

Stage 3 separates three protections that are often incorrectly treated as one:

1. **Logical-operation uniqueness**: PostgreSQL's `uq_transfers_idempotency_key`
   permits one immutable logical request for each globally scoped key.
2. **Request identity**: `request_fingerprint` plus `fingerprint_version` stores
   a SHA-256 hash of the canonical client reference, decimal amount and
   uppercase currency. Equal keys with equal fingerprints replay the durable
   transfer; a different fingerprint is an explicit conflict.
3. **External-submission claim**: a conditional PostgreSQL update permits only
   the contender that observes `ReadyToSubmit/version=1` to commit
   `Submitting/version=2`. The provider is called only after that commit.

The first registration inserts `ReadyToSubmit` and the fingerprint in one
database save. A uniqueness race is handled only when PostgreSQL reports the
named idempotency index violation; the winner is then loaded and compared. Only
the caller that inserted the registration may attempt the submission claim, so
a duplicate observing an existing `ReadyToSubmit` row cannot steal authority
from the creator. The conditional update remains the durable final arbiter. A
database transaction is not held across provider I/O. A process that dies after
the claim remains in `Submitting`; Stage 3 deliberately does not reset it or
automatically repost it because that could duplicate an already dispatched
external operation.

```mermaid
sequenceDiagram
    participant A as Caller A
    participant DB as PostgreSQL
    participant B as Caller B
    participant P as MockProvider

    A->>DB: INSERT key, fingerprint, ReadyToSubmit
    B->>DB: INSERT same key, fingerprint
    DB-->>A: unique-key winner
    DB-->>B: named unique violation; load winner
    A->>DB: conditional claim ReadyToSubmit/version 1
    DB-->>A: ClaimAcquired
    B-->>B: replay observed durable state; never claim
    A->>P: SubmitAsync once
    P-->>A: provider result
    B-->>B: no provider call
```

The fake provider's process-local ledger is observation-only external truth. It
does not decide idempotency and cannot substitute for PostgreSQL across process
instances or restarts.
