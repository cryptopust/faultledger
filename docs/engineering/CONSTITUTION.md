# FaultLedger engineering constitution

## Purpose and present status

FaultLedger is a planned deterministic .NET engineering failure laboratory for distributed financial-style transaction orchestration. It studies correctness under uncertainty using synthetic data. It is not a payment processor, bank, wallet, card processor, real-money system, or integration with Visa, Mastercard, or real payment rails.

**Governance only:** no application behavior, transaction system, database schema, migrations, or runtime guarantees are implemented at this stage. The principles below constrain later work; they are not evidence that a system already satisfies them.

[AGENTS.md](../../AGENTS.md) governs agent behavior, conflict handling, priority, and authorized rule changes. Detailed policies expand this constitution without weakening it. [RULE_INDEX.md](RULE_INDEX.md) maps permanent identifiers to evidence requirements.

## Non-negotiable principles

### CORRECTNESS

Financial-style correctness and data integrity outrank convenience and cleverness. Represent exact financial values without binary floating point, carry currency, and make precision and canonicalization explicit. Errors must preserve meaningful distinctions instead of manufacturing certainty. See FL-RULE-001 and FL-RULE-011.

### DURABILITY

Correctness must survive retries, contention, independent instances, and restarts. An invariant that exists only in process memory is not durable. Idempotency and recoverable work require database-backed identity and deliberate transaction boundaries. See FL-RULE-002, FL-RULE-006, and FL-RULE-008.

### DETERMINISM

Failure behavior is selected explicitly, never probabilistically. Controlled time, coordinated contention, and named failure boundaries make failures reproducible. Passing by chance is not evidence. See FL-RULE-007, FL-RULE-012, and FL-RULE-016.

### NO UNSAFE REPOST

Unknown is a valid result. A timeout does not prove failure. When external acceptance may have occurred, automatic repost is prohibited. Lack of evidence is not evidence of non-submission; recovery must reconcile uncertainty rather than create a second side effect. See FL-RULE-003 and [FAILURE_MODEL.md](FAILURE_MODEL.md).

### AT-LEAST-ONCE ASSUMPTION

Callbacks and outgoing messages may be duplicated, delayed, reordered, or redelivered after a crash. Persist callback receipt before acknowledging it. Commit related local state and outbox intent atomically. Consumers tolerate duplicates without claiming exactly-once delivery across distributed boundaries. See FL-RULE-004 and FL-RULE-005.

### POSTGRESQL AUTHORITY

PostgreSQL is the durable source of truth. Enforce appropriate invariants with constraints and transactional operations rather than cache truth or application conventions. Tests of these guarantees must exercise PostgreSQL itself. See FL-RULE-002, FL-RULE-008, and FL-RULE-015.

### EXPLICIT STATE

State transitions are explicit, evidence-authorized operations. Ambiguity remains visible. Stale input must not silently regress a terminal state. Reconciliation and audit record what is known, not what is convenient to assume. See FL-RULE-003 and FL-RULE-011.

### TESTED CLAIMS ONLY

Implementation plus deterministic regression evidence is required for a behavioral guarantee. Documentation must separate plans, demonstrated behavior, and limitations. Logs, confidence, mocked mechanisms, or a successful HTTP result alone are insufficient. See FL-RULE-007, FL-RULE-009, and FL-RULE-015.

### SIMPLE ARCHITECTURE

Prefer explicit boundaries within one deployable modular application, pure domain logic, a small dependency set, and PostgreSQL. Add no infrastructure for imagined future scale. An outbox is not a requirement for Kafka or microservices. See FL-RULE-013 and FL-RULE-017.

### SYNTHETIC AND SECURE

Use synthetic data only. No real customer or financial data, production credentials, provider secrets, or private keys belong here. Security defaults should fail closed; laboratory status is not an excuse for unsafe tooling or leaking developer credentials. See FL-RULE-014 and [SECURITY.md](../../SECURITY.md).

## Change discipline

The ordered priorities and scope workflow live in [AGENTS.md](../../AGENTS.md). Conflicting requests require a named conflict and the smallest compliant alternative. Only an explicit request to change policy authorizes a policy amendment; ordinary implementation tasks do not. No agent may weaken a rule or checker to obtain a green result.

Completion requires applicable evidence, adversarial self-review, truthful limitations, and a detailed unexecuted commit proposal. [DEFINITION_OF_DONE.md](DEFINITION_OF_DONE.md) describes that gate. No formal compliance certification, production readiness, or real-money capability is claimed.
