# Machine-checkable rule index

Authority: [AGENTS.md](../../AGENTS.md). High-level principles: [CONSTITUTION.md](CONSTITUTION.md). This is the stable registry, not a claim of current implementation or a replacement for the source rules.

The table schema is exactly **Rule ID**, **Short rule**, **Severity**, **Where defined**, and **How later work can prove compliance**. Each rule has one row, a permanent `FL-RULE-NNN` identifier matching its heading in `AGENTS.md`, and a severity of `NON-NEGOTIABLE`, `STRICT`, or `GUIDELINE`. Do not put literal pipe characters inside cells. Source links must resolve within the repository. The governance verifier parses this table; it does not judge the adequacy of behavioral evidence.

`NON-NEGOTIABLE` permits no implementation-level exception. `STRICT` is a mandatory engineering default with only the explicit exception process in its source rule. `GUIDELINE` permits a documented contextual choice; no current major rule is downgraded to that category. Severity changes require explicit policy authorization, not a feature request.

| Rule ID | Short rule | Severity | Where defined | How later work can prove compliance |
| --- | --- | --- | --- | --- |
| FL-RULE-001 | No binary floating point for exact financial values | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Architecture](ARCHITECTURE_RULES.md) | Precision, currency, canonicalization, equality, invalid-input and rounding-rejection tests; inspect financial types |
| FL-RULE-002 | PostgreSQL is durable authority | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Database](DATABASE_RULES.md) | Durable-state inspection and restart evidence; cache loss does not change truth |
| FL-RULE-003 | No automatic repost after ambiguous transmission | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Failure model](FAILURE_MODEL.md) | Timeout-after-acceptance, lost-response, NotFound and lease-expiry tests inspect provider submission counts |
| FL-RULE-004 | At-least-once callbacks and durable inbox | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Database](DATABASE_RULES.md) | Duplicate/concurrent/late callbacks, durable receipt before acknowledgement, and recoverable processing tests |
| FL-RULE-005 | Atomic outbox and at-least-once delivery | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Failure model](FAILURE_MODEL.md) | State/outbox atomicity and publish-before-ack crash tests; safe consumer redelivery |
| FL-RULE-006 | Durable database-backed idempotency | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Database](DATABASE_RULES.md) | Constraint-backed same-key and conflicting-canonical-request races across instances/restarts |
| FL-RULE-007 | Deterministic failure testing | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Testing](TESTING_RULES.md) | Named explicit fault scenarios and reproducible boundary coordination; no probabilistic semantics |
| FL-RULE-008 | No process-local durable correctness | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Database](DATABASE_RULES.md) | Independent-process contention, stale-owner, and restart tests against one PostgreSQL database |
| FL-RULE-009 | Documentation cannot exceed evidence | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Review](CODE_REVIEW_RULES.md) | Map every guarantee to implementation and passing tests; label plans and limits |
| FL-RULE-010 | No automatic Git commits or publication | STRICT | [AGENTS.md](../../AGENTS.md), [Commit rules](COMMIT_RULES.md) | Explicit action authorization and Git inspection; proposals are not execution |
| FL-RULE-011 | Explicit evidence-authorized state transitions | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Failure model](FAILURE_MODEL.md) | Legal/illegal/replayed transition and stale-callback regression tests |
| FL-RULE-012 | Controlled time | STRICT | [AGENTS.md](../../AGENTS.md), [Testing](TESTING_RULES.md) | Clock dependency inspection and time-advance tests without actual waiting |
| FL-RULE-013 | Simple architecture and pure Domain | STRICT | [AGENTS.md](../../AGENTS.md), [Architecture](ARCHITECTURE_RULES.md) | Dependency-direction inspection and current-requirement decisions; no speculative infrastructure |
| FL-RULE-014 | Synthetic data and fail-closed security | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Security](../../SECURITY.md) | Review examples/config/logging, relevant security tests, and secret checks without overclaiming coverage |
| FL-RULE-015 | Real PostgreSQL persistence proof | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Testing](TESTING_RULES.md) | Integration tests use real migrations and PostgreSQL, not mocked concurrency mechanisms |
| FL-RULE-016 | Flaky tests are defects | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Testing](TESTING_RULES.md) | Coordinated reproducible tests; no arbitrary sleeps, hidden retries, or weakened assertions |
| FL-RULE-017 | Strict builds and justified centralized dependencies | STRICT | [AGENTS.md](../../AGENTS.md), [Architecture](ARCHITECTURE_RULES.md) | Inspect evaluated props/package policy; build/format with warnings as errors; review scoped suppressions |
| FL-RULE-018 | Deliberate transactions, migrations, and constraints | STRICT | [AGENTS.md](../../AGENTS.md), [Database](DATABASE_RULES.md) | Migration tests and transaction/crash/recovery records tied to named database invariants |
| FL-RULE-019 | Focused scope and explicit policy changes | NON-NEGOTIABLE | [AGENTS.md](../../AGENTS.md), [Review](CODE_REVIEW_RULES.md) | Compare requested scope to full diff; report out-of-scope findings and authorization for policy amendments |
| FL-RULE-020 | Validation and adversarial completion evidence | STRICT | [AGENTS.md](../../AGENTS.md), [Definition of Done](DEFINITION_OF_DONE.md) | Actual command results, self-review, untracked-file inspection, and honest blocker/applicability records |
| FL-RULE-021 | Detailed reviewable commit proposals | STRICT | [AGENTS.md](../../AGENTS.md), [Commit rules](COMMIT_RULES.md) | WHY-focused Conventional Commit proposal with files, tests, impacts, limits, and unexecuted commands |

Future stages record evidence for applicable rows; this registry does not automatically mark any rule satisfied. Use [AGENT_COMPLIANCE_CHECKLIST.md](AGENT_COMPLIANCE_CHECKLIST.md) and the complete source policies during review.
