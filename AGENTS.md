Any coding agent modifying this repository MUST read this entire file before making changes.

# FaultLedger agent rules

Repository rules remain active across tasks. A task prompt does not silently override engineering invariants.

## 1. Authority and current scope

FaultLedger is an **engineering failure laboratory**: a planned deterministic .NET laboratory for studying failure modes in distributed financial-style transaction orchestration. It is not a payment processor, bank, wallet, card processor, or service connected to Visa, Mastercard, or real payment rails. It is not authorized to move real money. Real customer data and production secrets do not belong here.

This bootstrap contains governance and governance validation only. There is no application, solution, project, domain model, endpoint, schema, migration, application test suite, or Docker service. Descriptions of future behavior below are requirements, not implemented guarantees. Do not create those components until a subsequent task explicitly requests them.

Repository policy operates within the host's system/developer instruction and safety hierarchy; this file does not redefine that hierarchy. Within repository governance:

1. This file is authoritative for agent behavior and the stable rule identifiers below.
2. [CONSTITUTION.md](docs/engineering/CONSTITUTION.md) states the non-negotiable engineering principles.
3. The specialized engineering documents expand these rules; they do not grant exceptions to them.
4. [RULE_INDEX.md](docs/engineering/RULE_INDEX.md) is the machine-checkable rule registry, not an alternate source of policy.
5. Nested instructions may refine local practice but must not silently weaken root invariants.

When a requested implementation conflicts with an invariant, identify the conflict, stop the conflicting implementation, name the exact rule that would be violated, and propose the smallest compliant alternative. Continue only non-conflicting work where it remains meaningful. Conflicting documents are a reason to resolve the policy, not choose the weaker interpretation.

Agents may alter repository rules only when explicitly instructed to modify the rules themselves. Do not quietly weaken a rule, its severity, a checker, or an assertion to complete a task. An authorized policy change must explain the old rule, new rule, rationale, risks, evidence, and affected cross-references. A general request to implement a feature is not authorization to change policy.

## 2. Decision priority

1. Financial-style correctness
2. Data integrity
3. Deterministic evidence
4. Failure safety
5. Security
6. Maintainability
7. Simplicity
8. Performance
9. Developer convenience
10. Cleverness

When goals conflict, the higher-ranked goal wins. A different ordering requires an explicitly authorized, documented policy decision; it cannot implicitly waive a non-negotiable invariant or the laboratory's security boundary.

```text
Correctness > convenience.
Evidence > confidence.
Durability > process-local assumptions.
Explicit state > implied state.
Database invariants > application conventions.
Deterministic reproduction > probabilistic testing.
Simple architecture > fashionable architecture.
```

## 3. Task workflow and scope

Follow: **requested scope -> inspect -> implement only required scope -> test -> self-review -> stop**.

Before changing anything, inspect the working tree, relevant existing files, and all applicable nested `AGENTS.md` files. Preserve existing user work and local configuration. Do not overwrite, reset, clean, or discard unrelated changes. Initialize Git on `main` only when the directory is not already a repository; never destructively reinitialize an existing repository.

No opportunistic refactoring, unrelated naming cleanup, unrelated formatting, dependency upgrades without task necessity, or replacement of working architecture based on preference. Report unrelated defects as **OUT-OF-SCOPE FINDING** and leave them unchanged. If one demonstrably blocks correctness or compilation, explain the dependency and make only the minimum necessary fix; do not expand into general cleanup.

Read the relevant detailed policies before implementation:

| Area | Policy |
| --- | --- |
| Principles and rule registry | [Constitution](docs/engineering/CONSTITUTION.md), [Rule index](docs/engineering/RULE_INDEX.md) |
| Boundaries and dependencies | [Architecture rules](docs/engineering/ARCHITECTURE_RULES.md) |
| Persistence and contention | [Database rules](docs/engineering/DATABASE_RULES.md) |
| Ambiguity and delivery | [Failure model](docs/engineering/FAILURE_MODEL.md) |
| Behavioral evidence | [Testing rules](docs/engineering/TESTING_RULES.md) |
| Review and completion | [Code review](docs/engineering/CODE_REVIEW_RULES.md), [Definition of Done](docs/engineering/DEFINITION_OF_DONE.md), [Agent checklist](docs/engineering/AGENT_COMPLIANCE_CHECKLIST.md) |
| Collaboration and Git | [Contributing](CONTRIBUTING.md), [Commit rules](docs/engineering/COMMIT_RULES.md) |
| Security boundary and reporting | [Security policy](SECURITY.md) |

## 4. Stable rules

These identifiers are permanent. Do not renumber or reuse retired identifiers. The registry records severity and required evidence. Non-negotiable rules have no implementation-level convenience exception.

### FL-RULE-001 - Exact, currency-bearing financial values

Never use `float` or `double` for money, fees, balances, amounts, or rates representing exact financial values. Use exact decimal representation where appropriate and document range/overflow handling. Every monetary value carries currency explicitly. Never assume all currencies have two decimal places without an explicit, tested currency policy.

Never silently round input. Reject unexpected precision or apply a documented deterministic policy authorized for that boundary; implicit rounding is not such a policy. Parsing, serialization, equality, and canonicalization must be culture-independent where required. Test precision, currency mismatch, canonicalization, equality, invalid currency, and rounding rejection. See [Architecture rules](docs/engineering/ARCHITECTURE_RULES.md).

### FL-RULE-002 - PostgreSQL is durable authority

PostgreSQL is the durable source of truth for operation existence, state, idempotency, inbox, outbox, and audit history. Redis, logs, caches, or process memory must not become authoritative for them. A different authority requires an explicit policy redesign with documented proof, not an implementation shortcut. See [Database rules](docs/engineering/DATABASE_RULES.md).

### FL-RULE-003 - No automatic repost after ambiguous transmission

**Failure to receive a successful response does NOT prove an external operation failed.** If submission may have occurred, represent `UNKNOWN` or equivalent explicit ambiguity. **NEVER AUTOMATICALLY REPOST** unless the system can prove the original submission did not happen, including excluding an earlier in-flight attempt.

A timeout, HTTP 500, connection reset, lost response, local rollback, expired lease, restart, or provider `NotFound` is not that proof. Provider idempotency support alone is not an exception to this repository's no-repost rule. Query, reconcile, wait for an authenticated callback, or escalate for manual review. Manual review is not blanket permission to duplicate an operation.

This rule applies to HTTP resilience middleware, workers, SDK defaults, operator tooling, inbox/outbox consumers, and restart recovery. A rule permitting delivery retries does not permit a new external submission. See [Failure model](docs/engineering/FAILURE_MODEL.md).

### FL-RULE-004 - At-least-once callbacks and durable inbox

Assume callbacks can arrive twice, 100 times, concurrently, late, or out of order. Never claim exactly-once callback delivery. Authenticate, minimally validate the envelope, persist an inbox record, commit, acknowledge receipt, then process recoverably. Do not acknowledge success before durable receipt. Duplicate delivery must not duplicate a business effect. Processing must not depend on the callback HTTP connection remaining alive.

### FL-RULE-005 - Atomic outbox and at-least-once delivery

When an outgoing message must accompany a local state change, create the outbox record in the **same database transaction** as that change. Never commit state and merely hope publication succeeds. Assume publication may succeed while recording its acknowledgement fails. Redelivery is expected; consumers must tolerate duplicates.

Do not claim exactly once across distributed boundaries. Replay of a message must recover the same durable operation, not bypass FL-RULE-003 or create another provider POST. An outbox does not justify adding a broker. See [Database rules](docs/engineering/DATABASE_RULES.md) and [Failure model](docs/engineering/FAILURE_MODEL.md).

### FL-RULE-006 - Durable database-backed idempotency

Idempotency must be durable, database-backed, restart-safe, and multi-instance-safe. One key, within an explicitly documented scope, maps to one immutable canonical logical request. Same key plus same canonical request recovers the existing operation; same key plus a different canonical request returns an explicit conflict. Never reinterpret a key silently.

Handle client/network retries, concurrent requests, restarts, multiple processes, callback replay, and worker retries without duplicating external side effects. `SELECT` then `INSERT` alone is not a concurrency guarantee; enforce uniqueness in PostgreSQL and handle conflicts deterministically.

Fingerprints must be deterministic, versioned, canonical, and stable. Use a cryptographic hash such as SHA-256 where appropriate. Include stable business fields and exclude volatile timestamps, generated IDs, trace IDs, attempt counters, server correlation IDs, and provider responses. Document invariant canonical serialization and compatibility for existing fingerprint versions. See [Database rules](docs/engineering/DATABASE_RULES.md).

### FL-RULE-007 - Deterministic failure evidence

Failure behavior must never depend on `Random`, `Random.Shared`, GUID-based probabilistic branching, percentages, random timing, or randomized concurrency. Every injected failure is explicitly selected and reproducible with the same inputs. Random identifiers must not determine scenario selection or assertions; provide controlled values wherever identity affects reproducibility.

A failure-handling feature is implemented only when a deterministic automated regression test proves its observable behavior and invariants. Do not mock away the mechanism being tested. See [Testing rules](docs/engineering/TESTING_RULES.md).

### FL-RULE-008 - No process-local durable correctness

`lock`, `Monitor`, `SemaphoreSlim`, `Mutex`, `ConcurrentDictionary`, static state, and singleton state are not ultimate correctness boundaries across processes, replicas, or restarts. They may support local mechanics only when durable correctness does not rely on them.

Use appropriate PostgreSQL unique constraints, transactions, conditional updates, optimistic concurrency, row locks, or documented leases. `FOR UPDATE` and `SKIP LOCKED` are tools, not automatic correctness proofs. No homemade distributed lock. No Redis lock as a financial correctness boundary under current policy; a proposed exception requires explicit rule redesign and proof PostgreSQL cannot enforce the invariant. Lease expiry never proves an external submission did not occur.

### FL-RULE-009 - Documentation cannot exceed evidence

Documentation describes actual behavior. Claims require implementation plus passing relevant tests, with limitations stated. Use **planned**, **not yet implemented**, and **known limitation** accurately. README content is not marketing. Never imply this laboratory is a production-ready payment processor, PCI-compliant platform, bank-grade system, exactly-once payment system, or financially certified product.

### FL-RULE-010 - No automatic Git publication

Do not `git commit`, `git push`, `git tag`, create a GitHub release, force push, or create branches unless the user explicitly requests that action. The new-repository initialization exception is `git init -b main` when requested by bootstrap. Do not stage changes without authorization; a requested commit proposal is not authorization to stage or commit. Inspection commands such as `git status`, `git diff`, `git diff --check`, `git log`, and `git show` are permitted. See [Commit rules](docs/engineering/COMMIT_RULES.md).

### FL-RULE-011 - Explicit, evidence-authorized state transitions

State must not be an arbitrary mutable string or an unrestricted assignment such as `transfer.State = ...`. Future state changes are explicit domain operations. Each transition defines its source, target, legal reason, authorizing evidence, terminal status, and replay behavior.

Stale data or callbacks must not regress a terminal state, for example `Completed -> Accepted`. Any demonstrated need to leave a terminal state must be explicitly modeled, reviewed, and tested, not implemented as an unrestricted setter. `UNKNOWN` is an honest uncertainty state, not ordinary rejection. Provider `NotFound` is not proof of non-acceptance. Reconciliation distinguishes confirmed accepted, completed, rejected, not found, and still unknown.

### FL-RULE-012 - Controlled time

Business logic must not scatter `DateTime.UtcNow` or `DateTime.Now`, or hide uncontrolled clocks in helpers. Use `TimeProvider` or a controlled time abstraction at boundaries; pure domain operations receive explicit time values or pure abstractions. Tests control time deterministically. Do not actually wait to simulate time passage when controlled time suffices.

### FL-RULE-013 - Simple architecture and pure domain

Default to a simple modular monolith: a single deployable application, PostgreSQL durable authority, and explicit boundaries. Do not prepare for imaginary scale.

Unless a concrete **current** requirement proves necessity and a written decision is approved, prohibit Kafka, RabbitMQ, MassTransit, NServiceBus, Kubernetes, service mesh, Dapr, Orleans, Akka.NET, MediatR, generic repository frameworks, event-sourcing frameworks, CQRS frameworks, microservice decomposition, GraphQL, gRPC, NoSQL databases, and ElasticSearch. A distributed cache as system of record and custom distributed locks remain prohibited by FL-RULE-002/008; a dependency justification cannot waive them.

Future Domain code may depend only on appropriate BCL primitives and pure internal domain abstractions. It must not depend on ASP.NET Core, EF Core, Npgsql, Redis, HTTP, Docker, the OpenTelemetry SDK, filesystem access, environment variables, configuration frameworks, or logging frameworks. Infrastructure adapts to the domain, not the reverse. See [Architecture rules](docs/engineering/ARCHITECTURE_RULES.md).

### FL-RULE-014 - Synthetic data and fail-closed security

No real financial/customer data, real account identifiers, real provider secrets, production credentials, private certificates, or private keys. Never commit credential-bearing `.env` files. Examples must be obviously synthetic; fake credentials must not resemble leaked real credentials. Use sensible fail-closed defaults. The example password is a placeholder, not an application default or an authentication bypass. See [SECURITY.md](SECURITY.md).

### FL-RULE-015 - Real persistence proof

Database correctness tests use real PostgreSQL, preferably via Testcontainers, and actual migrations when schema behavior matters. Mocked EF `DbContext`, in-memory databases, SQLite substitutes, and mocked locking are not proof of PostgreSQL constraints, transactions, isolation, or concurrency. Important invariants need actual contention, independent service containers/processes where relevant, and restart tests against the same database.

### FL-RULE-016 - Flaky tests are defects

No `Thread.Sleep`, arbitrary `Task.Delay`, random timing, or randomized concurrency as synchronization. Use `Barrier`, `CountdownEvent`, `TaskCompletionSource`, `Channel`, controlled `TimeProvider`, and explicit boundary hooks. Bounded timeouts may stop hung tests; they do not prove that an event happened. Truly unavoidable real-time checks require a documented reason and isolation from deterministic correctness proof.

An intermittent failure means stop, reproduce, diagnose, and fix. Do not weaken assertions, add sleeps, retry until green, or call it "just flaky". See [Testing rules](docs/engineering/TESTING_RULES.md).

### FL-RULE-017 - Strict builds, small dependencies, shared style

Prefer BCL, then ASP.NET Core/.NET framework capability, then an existing dependency, and only then a justified new dependency. Centralize versions in `Directory.Packages.props`; no per-project version overrides or speculative packages. Do not use preview packages without explicit justification.

A major dependency needs a written decision containing **Problem**, **Why BCL/framework capability is insufficient**, **Why existing dependencies are insufficient**, **Operational cost**, **Failure modes introduced**, and **Removal cost**. Popularity, possible future utility, aesthetic preference, or saving 20 lines is insufficient.

Enable nullable reference types, implicit usings, warnings as errors, current SDK analyzers, build-time style enforcement, and deterministic compilation through `Directory.Build.props`. No global warning suppression or quiet property overrides. A suppression targets one warning, is local where possible, and has a written reason. Avoid null-forgiving `!` unless the invariant is obvious and documented.

`.editorconfig` is the Rider/CLI style authority: UTF-8, LF, final newline, four-space C# indentation, file-scoped namespaces, standard names, sensible `var`, focused types, explicit domain names, and clear control flow. Prefer readable control flow to clever LINQ. `global.json` pins the verified stable SDK; changing it is an explicit scoped dependency/toolchain decision.

### FL-RULE-018 - Deliberate persistence boundaries

Schema evolution requires migrations, not `EnsureCreated`, manual editing, or startup SQL hacks. Every unique constraint maps to an invariant; every important index has a correctness or measured access-path reason. Important transactions document what becomes durable, what can crash next, and how recovery works.

Do not casually wrap external network calls in transactions or hold PostgreSQL locks across arbitrary remote I/O. Any unavoidable exception needs a current requirement and written justification. Inbox/business completion and outbox/state changes require recoverable atomic boundaries. See [Database rules](docs/engineering/DATABASE_RULES.md).

### FL-RULE-019 - Focused scope and explicit policy changes

Follow the scope workflow in section 3. Do not expand implementation, silently relax governance, add unrequested infrastructure, or treat later-stage plans as instructions to implement them now.

### FL-RULE-020 - Validation, self-review, and completion evidence

Run applicable validation; do not merely predict success. Use [Definition of Done](docs/engineering/DEFINITION_OF_DONE.md) and the unchecked [Agent compliance checklist](docs/engineering/AGENT_COMPLIANCE_CHECKLIST.md). A review aid is not automatically satisfied by a checker.

For future code tasks, run the relevant explicit solution/project equivalents of `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes`. Run `docker compose config` when Compose exists, and always inspect `git diff --check`, `git diff --stat`, and `git status --short`. Review staged changes if present. Ordinary diff does not include untracked files: inspect their contents directly.

Investigate failures, fix relevant defects, and rerun. Do not finish with known relevant failures except a proven environmental blocker, reported with command, error, impact, and unverified claims. A blocker does not become a passing check or satisfied Definition of Done. Record genuinely inapplicable checks and why; do not create projects or services merely to make commands applicable.

This governance stage runs `powershell -NoProfile -File scripts/Verify-Governance.ps1 -BootstrapOnly` on Windows, or `pwsh -NoProfile -File scripts/Verify-Governance.ps1 -BootstrapOnly` where PowerShell 7 is available. The normal command without `-BootstrapOnly` remains usable after projects are introduced. The verifier checks structure, configuration, rule references, and selected hygiene, **not behavioral compliance or security completeness**.

### FL-RULE-021 - Reviewable commit proposals

After each implementation stage, provide a **COMMIT PROPOSAL** with a Conventional Commit-style title and a meaningful body explaining why. Include problem addressed, engineering decision, invariants affected, key changes, regression tests, validation executed, database/migration impact, operational impact, known limitations, files included, and suggested `git add` / `git commit` commands. Do not execute them without authorization. Separate logically independent changes. See [Commit rules](docs/engineering/COMMIT_RULES.md).

## 5. Runtime engineering rules for later stages

These are obligations for requested future implementation, not permission to build it now.

### Exceptions, asynchronous work, and public boundaries

Never use empty `catch (Exception) { }`, silently ignore failures, or collapse distinct outcomes to `false`, `null`, or generic failure. Expected categories are explicit; unexpected failures remain observable. Cancellation is not automatically provider rejection, nor proof of non-submission. Preserve uncertainty if cancellation follows possible external dispatch.

Use async all the way for I/O. Avoid `.Result`, `.Wait()`, and `GetAwaiter().GetResult()` in ordinary application logic. Propagate `CancellationToken` across I/O. No unobserved fire-and-forget tasks. Background workers have explicit lifetimes, recovery paths, and error handling; request cancellation cannot erase durable work already accepted.

Do not expose domain/database entities through HTTP endpoints. Use explicit request/response contracts and meaningful error categories. Do not expose internal stack traces. Configuration uses standard .NET conventions, such as `ConnectionStrings__Postgres`, outside the pure domain; do not hardcode environment-specific values.

### Logging, observability, and audit

Use structured logging with stable message templates, not concatenated payloads, for example `"Transfer {TransferId} entered state {State}"`. Never log passwords, secrets, authorization headers, private keys, credential-bearing connection strings, raw financial-style payloads, real account identifiers, PAN, or CVV. Synthetic data does not justify logging everything.

Important future flows need structured logs, trace spans, metrics, and durable audit history. Propagate correlation identifiers where useful without making them business identity or fingerprint input. Each metric answers an operational question. Avoid high-cardinality or sensitive metric labels. Logs do not replace metrics/traces, and none replaces the authoritative durable audit record.

Important state changes require durable append-only audit history, distinct from inbox receipt, outbox integration messages, and application logs. Add this only in an implementation stage that requests the relevant behavior, with atomicity and retention documented.

### Comments, unfinished work, Docker, and CI

Comments explain why, an invariant, a failure boundary, or a non-obvious tradeoff; do not narrate obvious code. No casual `TODO`, `FIXME`, "fix later", or "hack for now". Deliberate incomplete work belongs in known limitations or an actionable item with scope and completion criteria.

Never commit `.idea/`, nested `.idea/`, `*.sln.iml`, `workspace.xml`, or user-specific Rider settings. Preserve local IDE files, but keep them ignored. Share `.editorconfig`, build props, and central package policy instead.

Do not create Docker complexity until its component exists. Future Docker configuration avoids unnecessary exposed ports and baked-in secrets, is deterministic, and has meaningful health checks where applicable. `.env.example` is an obvious non-secret example only; loading it must not silently authorize a provider integration.

Local and CI validation must agree: restore, build, test, and format verification with the same checked-in SDK/configuration and documented commands. No pipeline-only magic. No CI pipeline or Docker services are claimed to exist in this bootstrap.

## 6. Mandatory adversarial self-review

After every implementation task, inspect the complete actual diff and untracked files. Ask:

- What invariant could this violate? What happens under concurrency or after restart?
- What happens if the network response disappears, or a DB write succeeds and the process dies next?
- What happens if this message/event arrives twice or out of order?
- Did I accidentally create process-local correctness or unnecessary architecture?
- Did I change anything outside scope or claim more than implementation and tests prove?

Fix confirmed in-scope problems before completion. Use the expanded [Code review rules](docs/engineering/CODE_REVIEW_RULES.md), report blockers and limitations honestly, provide the detailed commit proposal, and **stop at the requested boundary**.
