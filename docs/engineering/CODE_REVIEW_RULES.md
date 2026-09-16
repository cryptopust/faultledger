# Code review rules

Authority: [AGENTS.md](../../AGENTS.md). Principles: [CONSTITUTION.md](CONSTITUTION.md). Completion gate: [DEFINITION_OF_DONE.md](DEFINITION_OF_DONE.md).

Review is adversarial examination of evidence, not confirmation that the author feels confident. These rules apply to human review and mandatory agent self-review. Documentation/configuration changes also need applicable review; do not invent application tests for this governance-only stage.

## Mandatory reviewer questions

- Can this duplicate a side effect? Can a timeout create a second submission? Does any HTTP client, SDK, retry policy, worker, outbox consumer, or operator path bypass FL-RULE-003?
- Can this break under two processes? Can this break after restart? Did a singleton, static dictionary, `SemaphoreSlim`, or local lock accidentally become the correctness mechanism?
- Can stale data regress state? Can a callback arrive twice? Can this message redeliver or arrive out of order? What evidence authorizes each transition?
- Does a DB constraint enforce the claimed invariant? Are transaction boundaries, affected-row checks, stale ownership, commit uncertainty, and migration behavior deliberate?
- Is the test proving behavior or mocking it? Does it exercise real PostgreSQL, actual contention, independent instances, relevant restart/crash windows, and external simulator counts?
- Is this abstraction required today? Does the change add prohibited architecture, speculative dependencies, or unrelated cleanup?
- Is documentation claiming more than tests prove? Are plans, failures, blockers, and limitations labelled honestly?

For each material answer, identify the rule, implementation location, test/evidence, or unresolved gap. A checkbox or assertion of confidence is not evidence.

## Failure and concurrency review

Ask what happens when the network response disappears; when a DB write succeeds and the process dies next; when publication succeeds but its acknowledgement is lost; and when the database itself is unavailable. Examine the actual transaction and external-dispatch boundaries rather than just exception handlers.

Verify that provider `NotFound`, lease expiry, cancellation, restart, and local rollback do not imply permission to repost. Confirm that queries and message redelivery are distinguished from external submission retries. Check that a prior in-flight sender cannot be ignored when replacing ownership.

Inspect tests for accidental serialization, mocked locks, retained application memory, arbitrary sleeps, retry-until-green behavior, and weakened assertions. Independent dependency-injection containers are not proof of process isolation if static state is shared. Graceful shutdown is not an abrupt-crash test.

## Data, code, and security review

Verify exact currency-bearing financial values; no money-related `float`/`double`; explicit precision/canonicalization; controlled time; and deterministic failure selection. Confirm Domain purity and explicit public contracts.

Check async/cancellation propagation, observed worker lifetimes, meaningful exception categories, structured logging, secret/payload redaction, useful metrics/traces, and distinct append-only audit history where implemented. Unexpected errors must remain observable; no empty catches or false certainty.

Check package versions, SDK changes, warning/nullability suppressions, `.editorconfig`, and Rider/secret exclusions. A green build obtained by disabling analyzers or correctness assertions is a review failure.

## Governance adversarial checks

Reject interpretations that allow any of the following:

| Attempted shortcut | Governing rule |
| --- | --- |
| "Retry" means retry an ambiguous financial-style POST | FL-RULE-003 |
| Redis locks avoid designing a database invariant | FL-RULE-002, FL-RULE-008 |
| Mock PostgreSQL and claim concurrency safety | FL-RULE-015 |
| Call callback/outbox delivery exactly once | FL-RULE-004, FL-RULE-005 |
| Hide a race with `SemaphoreSlim` or sleeps | FL-RULE-008, FL-RULE-016 |
| Document a guarantee without implementation and tests | FL-RULE-009 |
| Add Kafka because an outbox exists | FL-RULE-013, FL-RULE-017 |
| Silently modify `AGENTS.md` to unblock a task | FL-RULE-019 |
| Interpret provider `NotFound` as never accepted | FL-RULE-003, FL-RULE-011 |

Look for contradictory detail documents, loopholes, impossible requirements, silent precedence changes, and policies that force speculative implementation. Fix in-scope contradictions without weakening invariants. An explicitly authorized policy amendment must update every affected reference and checker, explain why, and remain reviewable.

## Diff and disposition

Inspect the complete working-tree and staged diff, plus untracked file contents that ordinary `git diff` omits. Preserve unrelated user work. Report unrelated discoveries as **OUT-OF-SCOPE FINDING**, making only a documented minimum blocking fix when correctness or compilation demonstrably requires it.

Fix confirmed in-scope problems, rerun applicable validation, and record evidence before completion. Proven environmental blockers remain blockers, not passes. Use [AGENT_COMPLIANCE_CHECKLIST.md](AGENT_COMPLIANCE_CHECKLIST.md) as an unchecked review aid and provide the commit proposal required by [COMMIT_RULES.md](COMMIT_RULES.md). Do not stage or publish without authorization.
