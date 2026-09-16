# ADR 0003: Deterministic provider failure model

## Context

Stage 2 needs evidence that distinguishes a provider operation that definitely
did not exist from a provider operation that may already exist even when the
caller receives a timeout, connection loss, server error, or unusable response.
The existing provider contract only returned a reference on success and could
not express that distinction.

## Decisions

1. `SyntheticTransferProvider` has an explicit `MockProviderScenario` selector.
   Scenarios are selected by test construction; the production composition root
   uses `Success`. Unknown values fail closed and no random failure selection is
   permitted.
2. The provider has a visible acceptance boundary. Before the boundary, no
   accepted provider operation exists. After it, the process-local fake ledger
   contains a synthetic provider reference even if the caller-visible response
   is a failure or ambiguous result.
3. `ProviderSubmissionResult` carries typed `ProviderAcceptanceEvidence` and
   `ProviderFailureKind`. It cannot be reduced to `bool Success` or exception
   message parsing.
4. The process-local `MockProviderLedger` records submission attempts and
   accepted operations independently. It represents fake external truth only;
   PostgreSQL remains FaultLedger's durable authority and this ledger is not an
   idempotency or duplicate-suppression mechanism.
5. Slow responses use a `TaskCompletionSource` gate. Tests coordinate the
   decisive interleaving and do not use random timing or arbitrary sleeps.
6. Automatic provider retry/repost is absent. Stage 2 does not interpret
   ambiguity as a final local `Unknown` state and does not implement
   reconciliation, callbacks, inbox, outbox, or durable idempotency replay.

## Consequences

The provider-only tests can prove the difference between one attempted call and
one accepted provider operation without requiring Docker or a second database.
After-accept failures intentionally leave the application unable to claim a
successful response; later stages must reconcile the durable dispatch intent.
The fake ledger is process-local by design, so its thread safety is laboratory
evidence only and cannot be generalized to multi-instance FaultLedger
correctness.

## Alternatives rejected

- Random or percentage-based failure injection: not reproducible and violates
  FL-RULE-007.
- A generic exception or `bool Success`: loses the acceptance distinction.
- HTTP-only scenario headers in the production API: unnecessary exposure of a
  laboratory control.
- Automatic retry after timeout/500/connection loss: conflicts with FL-RULE-003.
- A provider database: out of scope for this fake external model and would add
  unnecessary infrastructure before the requested application behavior.
