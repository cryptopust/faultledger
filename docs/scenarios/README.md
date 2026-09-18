# Scenario catalog

Status is evidence from this workstation on 2026-09-18. `VERIFIED` means the
named automated test executed. `IMPLEMENTED / BLOCKED` means the Docker-backed
PostgreSQL or Toxiproxy test exists but the Docker engine is unavailable.

| # | Scenario | Status | Evidence |
| -: | --- | --- | --- |
| 01 | successful submission | VERIFIED | `ValidCommand_PersistsSubmittingBeforeProviderThenAcceptedWithReference` |
| 02 | sequential idempotency replay | VERIFIED | `ExistingSameRequest_ReplaysDurableTransferWithoutSubmission` |
| 03 | idempotency conflict | VERIFIED | `ExistingDifferentRequest_ConflictsBeforeSubmission` |
| 04 | 100-way duplicate contention | IMPLEMENTED / BLOCKED | `SameIdempotencyKey_OneHundredConcurrentRequests_SubmitsProviderExactlyOnce` |
| 05 | mixed-fingerprint race | IMPLEMENTED / BLOCKED | PostgreSQL mixed-request contention test |
| 06 | timeout before acceptance | VERIFIED | MockProvider failure laboratory |
| 07 | timeout after acceptance | VERIFIED | MockProvider failure laboratory |
| 08 | connection failure before acceptance | VERIFIED | MockProvider failure laboratory |
| 09 | connection loss after acceptance | VERIFIED | MockProvider failure laboratory |
| 10 | provider 500 before acceptance | VERIFIED | MockProvider failure laboratory |
| 11 | provider 500 after acceptance | VERIFIED | MockProvider failure laboratory |
| 12 | malformed response after acceptance | VERIFIED | MockProvider failure laboratory |
| 13 | Unknown outcome | VERIFIED | `AmbiguousOutcome_ReplayAndReconciliationNeverResubmit` |
| 14 | reconciliation accepted | VERIFIED | reconciliation service tests |
| 15 | provider NotFound stays Unknown | VERIFIED | reconciliation service tests |
| 16 | eventual NotFound to Accepted | VERIFIED | reconciliation service tests |
| 17 | duplicate callback | VERIFIED contract; BLOCKED persistence | callback contract/PostgreSQL tests |
| 18 | concurrent duplicate callback | IMPLEMENTED / BLOCKED | PostgreSQL inbox contention test |
| 19 | out-of-order callback | VERIFIED domain; BLOCKED persistence | callback transition/PostgreSQL tests |
| 20 | callback processing rollback | IMPLEMENTED / BLOCKED | PostgreSQL rollback test |
| 21 | poison callback | IMPLEMENTED / BLOCKED | PostgreSQL poison test |
| 22 | callback/reconciliation race | IMPLEMENTED / BLOCKED | PostgreSQL race test |
| 23 | state and outbox atomic commit | IMPLEMENTED / BLOCKED | `CompletedTransition_CreatesOneStableOutboxMessage` |
| 24 | business/outbox rollback | IMPLEMENTED / BLOCKED | `CompletionTransactionRollback_LeavesTransferAndOutboxUnchanged` |
| 25 | dispatcher failure then retry | VERIFIED unit; BLOCKED persistence | dispatcher and PostgreSQL tests |
| 26 | crash before publish | IMPLEMENTED / BLOCKED | restart/claim recovery test |
| 27 | crash after publish before ack | IMPLEMENTED / BLOCKED | `CrashAfterPublishBeforeAck_RedeliversStableEventAndConsumerEffectIsOne` |
| 28 | duplicate event delivery | IMPLEMENTED / BLOCKED | consumer duplicate test |
| 29 | two-dispatcher contention | IMPLEMENTED / BLOCKED | `OneHundredMessages_TwoDispatchers_DeliverAllWithOneEffectEach` |
| 30 | poison outbox fairness | IMPLEMENTED / BLOCKED | `PoisonMessage_IsDeferredAndDoesNotStarveLaterMessage` |
| 31 | process restart | IMPLEMENTED / BLOCKED | PostgreSQL restart/reclaim tests |
| 32 | network latency | IMPLEMENTED / BLOCKED | Toxiproxy latency test |
| 33 | network unavailable | IMPLEMENTED / BLOCKED | Toxiproxy unavailable test |
| 34 | response-path loss after delivery | IMPLEMENTED / BLOCKED | Toxiproxy ambiguity test |

Detailed semantics: [mock-provider](mock-provider.md),
[outbox crash recovery](outbox-crash-recovery.md), and
[network failures](network-failures.md).
