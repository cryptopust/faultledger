# Scenario index

Stage 2 implements deterministic MockProvider failure scenarios and a separate
provider-side simulated ledger. The matrix and acceptance-boundary timelines
are documented in [mock-provider.md](mock-provider.md). Provider-only tests
cover every named scenario, the controlled slow-response gate, and concurrent
fake-provider ledger access. They do not prove PostgreSQL behavior or durable
FaultLedger idempotency.

See [testing rules](../engineering/TESTING_RULES.md), the conceptual
[failure model](../engineering/FAILURE_MODEL.md), and the current validation
records. Stage 4 implements explicit Unknown interpretation and provider lookup
reconciliation without automatic repost. Stage 5 adds the durable callback
inbox; Stage 6 adds the transactional outbox, recoverable dispatcher, durable
consumer deduplication, and Docker-required Toxiproxy scenarios. The network
scenarios remain blocked when Docker is unavailable. Scenario documents must
describe implemented behavior and distinguish blocked PostgreSQL evidence from
pure deterministic tests.
