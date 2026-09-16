# Scenario index

No financial/distributed-failure scenario is implemented in stage 0B.
The current automated scenarios concern project boundaries and API health:
missing/invalid configuration, refused PostgreSQL connections, live-versus-ready
semantics, and mandatory container-backed connectivity/lifecycle checks.

See [testing rules](../engineering/TESTING_RULES.md), the conceptual
[failure model](../engineering/FAILURE_MODEL.md), and the current
[validation record](../runbooks/bootstrap-validation.md). Future scenario documents
must describe implemented, deterministically tested behavior, not planned claims.
