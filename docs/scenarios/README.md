# Scenario index

Stage 2 implements deterministic MockProvider failure scenarios and a separate
provider-side simulated ledger. The matrix and acceptance-boundary timelines
are documented in [mock-provider.md](mock-provider.md). Provider-only tests
cover every named scenario, the controlled slow-response gate, and concurrent
fake-provider ledger access. They do not prove PostgreSQL behavior or durable
FaultLedger idempotency.

See [testing rules](../engineering/TESTING_RULES.md), the conceptual
[failure model](../engineering/FAILURE_MODEL.md), and the current
[validation record](../runbooks/stage1-validation.md). The application still
does not implement Unknown interpretation, reconciliation, callbacks,
inbox/outbox, or automatic repost. Future scenario documents must describe
implemented, deterministically tested behavior, not planned claims.
