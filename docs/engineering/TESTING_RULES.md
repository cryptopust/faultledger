# Testing rules

Authority: [AGENTS.md](../../AGENTS.md), especially FL-RULE-007, FL-RULE-015, FL-RULE-016, and FL-RULE-020. Related: [FAILURE_MODEL.md](FAILURE_MODEL.md), [DATABASE_RULES.md](DATABASE_RULES.md), and [DEFINITION_OF_DONE.md](DEFINITION_OF_DONE.md).

**Current status:** there are no application tests or projects. The governance verifier is a structural policy check, not a proof of financial, persistence, concurrency, or distributed behavior. The following tests become mandatory when the relevant implementation is requested.

## What counts as evidence

No feature is implemented merely because code exists. A failure-handling feature needs a deterministic automated regression test of observable behavior and invariants. Test the mechanism claimed, not an imitation that removes its failure modes. Record test names, commands, results, and remaining limitations.

Pure domain tests may use in-memory values. Database correctness requires real PostgreSQL, preferably provisioned with Testcontainers, and real migrations. Mocked EF `DbContext`, SQLite, EF in-memory storage, or mocked locks cannot establish PostgreSQL isolation, uniqueness, transaction, or concurrency behavior. Exercise the real HTTP application boundary when it contributes meaningful evidence.

Testcontainers and future test packages are not installed in this governance stage. Select justified compatible stable versions and deterministic image references only when adding the relevant tests. A missing Docker engine must produce a clear blocker, not a silently skipped green database suite.

## Deterministic scenarios and controlled time

Each scenario specifies inputs, provider behavior, relevant interleaving, failure boundary, and expected durable result. Inject faults by explicit scenario IDs or boundary hooks. Never select failure behavior with `Random`, `Random.Shared`, GUID probabilities, failure percentages, random timing, or randomized concurrency.

Use controlled `TimeProvider` or equivalent; advance test time rather than wait. Use explicit synthetic identifiers wherever identity affects outcome. Independent database names may isolate runs but must not select semantics. The OS scheduler need not be deterministic: the test must coordinate the decisive ordering instead of assuming a lucky schedule.

`Barrier`, `CountdownEvent`, `TaskCompletionSource` with appropriate continuation behavior, `Channel`, and explicit hooks are preferred coordination mechanisms. Do not turn a test-only gate into the application's concurrency protection. Release resources and workers on failure, and bound every wait with useful diagnostics.

## Flaky-test ban

Flaky tests are defects (FL-RULE-016). No `Thread.Sleep`, arbitrary `Task.Delay`, random scheduling, repeated runs until green, or reduced assertions to hide races. Bounded timeouts serve only as liveness watchdogs; timeout expiry is never evidence that a business event occurred.

A real-time readiness check may be justified when no controllable alternative exists; document its purpose, bound it, and wait for an explicit readiness condition rather than an arbitrary delay. Do not use this exception to test business-time passage or establish database contention.

On intermittent failure: **stop -> reproduce -> diagnose -> fix -> rerun**. Preserve the failing scenario and diagnostics. Quarantine or retries cannot substitute for fixing an invariant failure or truthfully reporting it as unresolved.

## Behavioral naming and exact values

Names tell the engineering story, for example:

```text
SameIdempotencyKey_ConcurrentRequests_SubmitsProviderExactlyOnce
TimeoutAfterProviderAcceptance_TransitionsTransferToUnknown
UnknownTransfer_ReconciliationConfirmsExistingOperation_DoesNotResubmit
DuplicateCallback_DoesNotApplyBusinessEffectTwice
OutOfOrderCallback_DoesNotRegressTerminalState
CrashAfterPublishBeforeMarkingOutboxComplete_RedeliveryRemainsSafe
```

Avoid `Test1`, `Works`, `HappyPath`, and `ServiceTest`. "ExactlyOnce" in the first example counts a provider submission in one controlled successful scenario; it does **not** claim exactly-once distributed delivery.

Money tests cover precision, supported and invalid currencies, currency mismatch, culture-independent parsing, canonicalization, equality, rejection of unexpected precision/rounding, and defined range/overflow behavior. Canonical request tests also prove fingerprint version compatibility and exclusion of volatile fields.

## High-contention standard

Where duplicate suppression is claimed, deliberately coordinate at least the relevant high-contention scenario, including 100 concurrent requests sharing one key and canonical request. Also race the same key with different canonical requests. `Task.WhenAll` alone is not evidence of overlap: use boundary instrumentation or coordination to show actual contention without serializing the correctness mechanism.

In a deterministic successful provider scenario, prove one durable logical operation, consistent recovery of the same result, and one provider submission. Conflicting canonical requests must produce an explicit loser/conflict with no additional provider submission. Under an ambiguous scenario, prove the applicable durable state and no unsafe repost instead of expecting clean success.

Inspect **both local PostgreSQL state and external MockProvider submission count/history**. Also inspect business effects, inbox/outbox records, and audit state where relevant. A successful HTTP result alone is insufficient. The provider simulator must distinguish receipt/submission, actual acceptance/effect, response delivery, and query observations; record them independently of the application lifetime.

## Multi-instance standard

Construct independent application/service containers sharing only the durable database and explicitly external simulator. Do not share singleton services, in-memory deduplication state, or a lock. Test both same-request and conflicting-request races across instances.

Independent containers in one process can still share static state. Use distinct processes when proving process isolation or when static/runtime state could mask a defect. State clearly whether the evidence is multi-container or multi-process; do not overclaim.

## Restart and crash standard

For restart durability: create state, dispose the application/services, create new services, reuse the same PostgreSQL database, and verify the invariant with no retained application memory. Keep provider evidence independently available across the restart.

Graceful disposal is not proof of abrupt-crash safety. Where crash recovery is claimed, also terminate the process or use a faithful explicit crash boundary that excludes cleanup/compensation, restart independently, and verify recovery. Record the tested boundary and any simulation limitation.

## Required failure windows

Use the safe outcomes in [FAILURE_MODEL.md](FAILURE_MODEL.md); target relevant windows explicitly:

| Window/scenario | Minimum observable proof |
| --- | --- |
| BEFORE external side effect | Proven no dispatch; recovery cannot race an earlier sender |
| DURING dispatch with uncertain acceptance | Ambiguity preserved; no automatic repost |
| AFTER provider acceptance but BEFORE local certainty | Existing provider effect; `UNKNOWN` or recoverable ambiguous intent; no second submission |
| AFTER local durable state | New process recovers committed state rather than duplicating work |
| AFTER publish but BEFORE acknowledgement | Redelivery is safe; logical consumer effect is not duplicated |
| Duplicate callback, including concurrent replay | Durable receipt and deduplicated business effect |
| Out-of-order callback | Terminal state does not regress; contradictory evidence is observable |
| Expired worker lease with a stale worker | Local stale writes rejected; no replacement external repost |
| Provider eventually consistent `NotFound` | Uncertainty remains; no new submission |
| DB unavailable or commit acknowledgement lost | No invented commit result or false callback acknowledgement |
| Network partition/cancellation | Transport outcome does not masquerade as provider rejection |

Fault hooks must target the actual persistence/dispatch/publish boundary. A generic thrown exception far from it is not enough. Do not test only clean success and clean rejection. Never require all scenarios for unrelated tasks: document which apply and why.

## Regression and local/CI enforcement

Fixes need a test that fails against the defect and passes with the fix, where reproducible. Preserve assertions around counts, transitions, and database invariants. Do not mock away the bug or blanket-skip required tests.

Future local and CI validation use the same SDK, checked-in configuration, documented restore/build/test/format commands, and real service prerequisites. Record pass, fail, blocked, or not applicable honestly. This stage has no CI workflow; [DEFINITION_OF_DONE.md](DEFINITION_OF_DONE.md) specifies the current executable checks and later gates.
