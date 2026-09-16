# Distributed failure model

Authority: [AGENTS.md](../../AGENTS.md), especially FL-RULE-003, FL-RULE-004/005, FL-RULE-006, FL-RULE-008, and FL-RULE-011. Related: [DATABASE_RULES.md](DATABASE_RULES.md) and [TESTING_RULES.md](TESTING_RULES.md).

This is a conceptual model for future deterministic simulation, not an implemented provider integration or a complete state machine.

> Unknown is a valid result.

> A timeout does not prove failure.

> When external acceptance may have occurred, automatic repost is prohibited.

## Vocabulary and governing distinction

**Submission** is a potentially side-effecting external request. **Acceptance** means the provider may have acted durably. **Local certainty** is supported by evidence that FaultLedger can recover, not merely a response held in process memory. **Delivery retry** repeats a message delivery; it must not become permission to create a new external submission.

`UNKNOWN` is a conceptual representation of uncertainty, not a domain type created here. If the database cannot record a new uncertainty state, do not pretend that it did. Preserve/recover prior durable dispatch intent and report the storage failure. Future designs must establish that intent before allowing external dispatch.

## Failure windows and safe defaults

| Failure window | What can safely be concluded? | Safe default engineering response |
| --- | --- | --- |
| Failure before external dispatch | No submission only if an explicit boundary proves dispatch never started and no earlier sender remains in flight | Recover local work by durable identity. A later dispatch requires that proof and the normal ownership/idempotency checks. |
| Failure during dispatch where acceptance cannot be proven | The provider may or may not have accepted | Preserve `UNKNOWN` or recoverable ambiguous intent; do not repost. Query/reconcile or wait for a callback. |
| Failure after external acceptance before response | A side effect may exist despite the lost response | Do not mark ordinary rejection. Reconcile the original identity; prove with simulator evidence that no second submission occurs. |
| Failure after local DB commit | Durable changes may exist even when the caller sees no success | Recover by stable database identity in a new instance; return/reconcile existing state, not a new operation. |
| Failure during DB commit acknowledgement | Commit may or may not have happened | Treat commit as uncertain; inspect durable identity on a valid connection before safe local replay. Never infer provider non-acceptance. |
| Failure after publication before local acknowledgement | The consumer may already have received/applied the message | Permit at-least-once redelivery with durable consumer deduplication; never bypass the provider no-repost rule. |
| Duplicate delivery | A durable receipt or logical business effect may already exist | Recover the same inbox/operation; do not repeat the effect. Conflicting content under the same identity is an explicit conflict. |
| Out-of-order delivery | The arrival order is not necessarily the business evidence order | Apply only legal evidence-authorized transitions; retain/observe stale or conflicting evidence without regressing terminal state. |
| Process crash | Memory, ownership assumptions, and uncommitted work are lost; remote work may continue | Recreate services against the same database, recover durable intent, reject stale owners, and preserve ambiguity. Do not rely on graceful shutdown. |
| Network partition | Neither remote liveness nor rejection can be inferred from silence | Fail closed for new unsafe dispatch, retain durable work, bound safe queries, and resume reconciliation after connectivity returns. |
| Database unavailable | New durable receipt/ownership/state may be impossible; earlier commits or dispatches may still exist | Do not acknowledge unpersisted callbacks or submit without required durable intent. Report the outage; recover by durable identity when storage returns. |
| Provider eventually consistent lookup | `NotFound` may reflect lookup lag rather than absence | Remain unknown/not found, repeat safe queries under controlled policy, wait for callbacks, or escalate; never automatically repost. |
| Lease expires while prior sender is alive | The old sender may still have an external call in flight | Reject stale local mutation with ownership/version checks; do not let replacement ownership authorize another submission. |
| Request cancellation after possible dispatch | The caller stopped waiting; provider outcome remains uncertain | Propagate cancellation appropriately without labelling provider rejection, losing durable work, or reposting. |

## Reconciliation evidence

Future reconciliation must distinguish at least:

```text
confirmed accepted
confirmed completed
confirmed rejected
not found
still unknown
```

Specify which authenticated, operation-correlated evidence authorizes each conclusion. A `NotFound` response, elapsed time, exhausted query budget, local missing row, or generic transport failure is not proof of non-submission. A confirmed rejection may authorize a modeled state change; it is not blanket permission to reuse the idempotency key or submit a new operation.

Define bounded query/backoff policies using controlled time and deterministic scenarios. Queries may be retried when they are truly read-only and replay-safe. Exhausting the query budget must retain uncertainty and an explicit escalation path rather than invent failure. Manual review may establish evidence; it does not waive FL-RULE-003.

## What the word "retry" does not authorize

The following reasoning is forbidden:

```text
request timed out -> retry POST
500 -> retry POST
connection reset -> send again
no local response -> assume failure
provider NotFound -> safe to submit again
lease expired -> replacement worker submits again
outbox redelivered -> submit a new provider request
provider supports idempotency -> ignore the no-repost rule
```

Transport errors do not identify the provider's commit boundary. Even a response classified as an error might follow a durable side effect. Disable or constrain generic HTTP/SDK resilience retries on submission paths when those paths are implemented, and test the actual configured transport stack.

Proof permitting dispatch after a prior failure must establish that the original submission did not occur and cannot still occur. A deterministic pre-dispatch boundary with no escaped request and no prior in-flight attempt is an example. Local transaction rollback alone is not. Document the evidence and regression test before permitting that path.

## Durable delivery boundaries

The conceptual inbox flow is:

```text
authenticate
  -> validate minimal envelope
  -> persist inbox
  -> commit
  -> acknowledge receipt
  -> process recoverably
```

Do not keep correctness tied to the original callback connection. Receipt can be replayed, processing can restart, and both require durable identity. Invalid/unauthenticated requests are rejected rather than persisted as trusted business facts.

The conceptual outbox flow is:

```text
local state change + required outgoing intent in one transaction
  -> commit
  -> publish recoverably
  -> record publication acknowledgement
```

Publishing and acknowledgement recording are separate failure windows. Expect redelivery, not exactly-once delivery. A message that requests external work must resolve to the same durable dispatch record and must never turn uncertain work into a fresh POST.

## Deterministic proof obligations

For each implemented scenario, select the failure explicitly and coordinate the precise boundary. Inspect PostgreSQL state and external simulator submission, acceptance/effect, and response histories. Include independent-instance contention and restart/crash evidence where claimed.

Test the distinction between **BEFORE side effect**, **AFTER side effect but BEFORE local certainty**, **AFTER local durable state**, and **AFTER publish but BEFORE acknowledgement**. One generic timeout test does not prove all windows. The complete standards and behavioral names are in [TESTING_RULES.md](TESTING_RULES.md).

## Known limits of this model

No provider contract, state graph, retention period, reconciliation schedule, schema, authentication mechanism, or crash harness is implemented yet. Conservative uncertainty may reduce availability; safety has priority. Never describe these rules as an implemented guarantee or as formal financial compliance.
