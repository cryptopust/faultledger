# Stage 1 validation record

The original Stage 1 bootstrap record is preserved in
[bootstrap-validation.md](bootstrap-validation.md). It documents the tooling
baseline and its Docker limitation; it does not claim that later application
behavior was proven.

For the current repository-wide evidence boundary, use the root
[README](../../README.md), [invariant catalog](../invariants.md), and
[test matrix](../test-matrix.md). PostgreSQL/Testcontainers and Toxiproxy tests
remain `BLOCKED BY ENVIRONMENT` when no Docker engine is available.
