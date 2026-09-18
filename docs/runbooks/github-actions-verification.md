# GitHub Actions verification record

## Purpose

This runbook records attempts to execute the Docker-backed correctness suite in
GitHub Actions. A workflow failure before runner startup is infrastructure
evidence only; it does not verify any repository behavior.

## Workflow

- Workflow: `FaultLedger Full Verification`
- Definition: `.github/workflows/ci.yml`
- Runner requested: `ubuntu-24.04`
- Intended checks: governance, pinned restore, build, unfiltered `dotnet test`,
  repeated full suite, format verification, Docker/Compose inspection, Compose
  validation, API image build, TRX artifact upload, and ten repetitions of the
  named critical Docker scenarios.

## Attempts

| Run | Commit | URL | Result | Evidence boundary |
| ---: | --- | --- | --- | --- |
| 35313203932 | `0d7b136ce40762f1e4a3cc26e8f6a3a73f05099e` | [run](https://github.com/cryptopust/faultledger/actions/runs/35313203932) | FAILED before job start | GitHub annotation: `The job was not started because your account is locked due to a billing issue.` |
| 35314827156 | `9ee38fc67dee374ef8016a6a419f46668a87bbed` | [run](https://github.com/cryptopust/faultledger/actions/runs/35314827156) | FAILED before job start | `validate` received the same billing annotation; dependent `critical-stress` was skipped. |

## Evidence status

The runner never started for either attempt. Therefore the following remain
`IMPLEMENTED BUT NOT VERIFIED`:

- PostgreSQL migrations and transaction/constraint behavior
- 100-way idempotency and mixed-fingerprint contention
- independent-host contention and restart recovery
- callback inbox persistence, duplicate/out-of-order processing, and races
- transactional outbox, dispatcher claims, crash windows, and consumer dedupe
- Toxiproxy latency, unavailable connection, and response-loss scenarios

Local Windows evidence remains limited to the Docker-excluded suite and other
checks listed in the root README. Do not infer Docker or PostgreSQL behavior
from source inspection, workflow configuration, or a job that never started.

## Required follow-up

Resolve the GitHub account billing/runner lock, then rerun the workflow at the
current commit. Promote a matrix row only after the corresponding test actually
passes in a started runner and retain the run ID, commit SHA, SDK version,
Docker/Compose versions, counts, and focused stress repetitions.
