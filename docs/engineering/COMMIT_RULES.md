# Commit and Git rules

Authority: [AGENTS.md](../../AGENTS.md), FL-RULE-010 and FL-RULE-021. Completion gate: [DEFINITION_OF_DONE.md](DEFINITION_OF_DONE.md).

## Authorization and preservation

Agents must not run `git commit`, `git push`, `git tag`, create releases, force push, or create branches unless the user explicitly requests that action. Requested initialization of a new repository on `main` is allowed; existing repositories must not be destructively reinitialized. Do not stage without authorization. A commit proposal authorizes neither staging nor committing, and committing does not automatically authorize pushing.

Read-only inspection includes `git status`, `git diff`, `git diff --check`, `git log`, and `git show`. Never discard unrelated work with reset, checkout/restore, or clean operations to simplify a task. Do not assume an empty ordinary diff means no changes: inspect untracked and already staged files.

## One reviewable engineering idea

Use Conventional Commit-style titles: `feat:`, `fix:`, `test:`, `docs:`, `refactor:`, `chore:`, or `ci:`, with a useful optional scope. Describe intent, not a vague inventory. Reject titles such as "update files", "fix stuff", or "changes".

A commit should represent one reviewable engineering idea. Do not mix architecture refactoring, dependency upgrades, a feature, unrelated formatting, and a documentation rewrite unless inseparable. Propose multiple logical commits when appropriate. Required tests and documentation supporting one change belong with that idea.

Meaningful changes require detailed bodies explaining **why**: the problem, engineering decision, alternatives/tradeoffs where useful, invariants, evidence, and limitations. Explain database/migration and operational effects explicitly, including "none" where true.

## Required proposal after each implementation stage

Produce a section named **COMMIT PROPOSAL**. It must contain:

```text
Title:

Body:

Problem addressed:
Engineering decision / philosophy:
Invariants affected:
Key changes:
Regression tests:
Validation executed:
Database/migration impact:
Operational impact:
Known limitations:
Files included:
Suggested git add command:
Suggested git commit command:
```

Use actual validation results, not commands that were merely recommended. State skipped/inapplicable checks and proven blockers separately. List intended files explicitly, distinguishing existing unrelated changes. Suggested staging should name the reviewed files rather than use broad `git add .` or `git add -A`.

The governance bootstrap uses the proposed title `chore: establish FaultLedger engineering constitution`. Its body must contain at least three useful paragraphs explaining the need for persistent engineering policy, the correctness/failure philosophy, and the fact that this changes policy/enforcement only with **no business implementation**. Mention governance validation honestly; do not claim application tests passed when none exist.

Suggested commands are instructions for a human to review, not actions the agent has taken. Use shell-appropriate quoting. A suggested commit may use repeated `-m` options for the title and body paragraphs; keep the detailed proposal consistent with that message.

## Before any subsequently authorized commit

Reinspect `git status`, the intended staged diff, whitespace checks, and file list. Ensure secrets, Rider metadata, generated outputs, and unrelated work are excluded. Honor the original scope and current authorization. Do not amend, tag, push, or create a release as an implied follow-on action.
