# Agent compliance checklist

Authority: [AGENTS.md](../../AGENTS.md). Rule mapping: [RULE_INDEX.md](RULE_INDEX.md). Completion criteria: [DEFINITION_OF_DONE.md](DEFINITION_OF_DONE.md).

This is an **unchecked review aid**, not automatic certification. Copy it into a stage review when useful and attach concrete evidence or a justified not-applicable explanation to each item. A structural checker does not establish behavioral compliance.

- [ ] Read all of `AGENTS.md` and applicable nested instructions.
- [ ] Read the relevant engineering policies.
- [ ] Scope stayed focused; existing user work was preserved.
- [ ] No prohibited architecture added without its explicit authorization process.
- [ ] No financial `float`/`double` introduced; currency and precision policy are explicit.
- [ ] No process-local durable invariant introduced.
- [ ] No unsafe retry/repost introduced; ambiguity and `NotFound` stay explicit.
- [ ] Idempotency and fingerprints are canonical, versioned, durable, and race-safe where applicable.
- [ ] Callback/inbox and outbox behavior tolerates at-least-once delivery where applicable.
- [ ] State transitions reject stale/illegal regressions where applicable.
- [ ] Tests are deterministic; clocks and failure selection are controlled.
- [ ] Real persistence and migrations are used where required.
- [ ] Concurrency was considered and tested under actual contention where claimed.
- [ ] Independent instances/processes were tested where claimed.
- [ ] Restart and abrupt-crash behavior were considered separately.
- [ ] Relevant external-dispatch, commit, publication, and acknowledgement windows were tested.
- [ ] Documentation matches actual evidence and distinguishes plans from guarantees.
- [ ] Dependencies, nullability, warnings, shared style, and Rider hygiene remain compliant.
- [ ] Full applicable validation executed; inapplicable commands and blockers are explained.
- [ ] Git diff, staged changes, and untracked file contents inspected.
- [ ] No secrets, real financial/customer data, or sensitive log payloads added.
- [ ] Adversarial self-review completed and confirmed in-scope defects fixed.
- [ ] Known limitations and OUT-OF-SCOPE FINDING items recorded.
- [ ] Detailed commit proposal produced with unexecuted staging/commit commands.
- [ ] No unauthorized staging, commit, push, tag, branch, or release action performed.

Do not pre-check this template in the repository. Preserve unresolved evidence gaps rather than labelling them passed. The next stage is not authorized merely because the current checklist is complete.
