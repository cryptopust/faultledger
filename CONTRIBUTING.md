# Contributing to FaultLedger

FaultLedger is an engineering failure laboratory using synthetic data, not a real-money payment system. The repository currently contains governance only; no application or database behavior is implemented.

Read all of [AGENTS.md](AGENTS.md), the [constitution](docs/engineering/CONSTITUTION.md), and relevant [engineering rules](docs/engineering/RULE_INDEX.md) before changing files. Task prompts do not silently override invariants. A conflict requires an explicit explanation and the smallest compliant alternative; only an explicit policy-change request authorizes changing the rules.

Keep scope focused. Inspect existing work first, preserve local/user changes, avoid unrelated cleanup and speculative dependencies, and report unrelated issues as **OUT-OF-SCOPE FINDING**. Add deterministic regression tests for behavior; use real PostgreSQL and migrations for database claims, actual contention for concurrency claims, and independent instances/restarts where required. Do not introduce application projects or services in the governance bootstrap.

## Tooling and validation

Use the SDK pinned in [global.json](global.json), shared [.editorconfig](.editorconfig), [Directory.Build.props](Directory.Build.props), and [Directory.Packages.props](Directory.Packages.props). Keep Rider `.idea/` and user-specific settings local. No packages are declared yet.

Current Windows validation starts with:

```powershell
powershell -NoProfile -File scripts/Verify-Governance.ps1 -BootstrapOnly
git diff --check
git diff --cached --check
git diff --stat
git status --short --untracked-files=all
```

Use `pwsh` instead of `powershell` where PowerShell 7 is available. Follow the complete [Definition of Done](docs/engineering/DEFINITION_OF_DONE.md) for SDK/property checks and future restore/build/test/format/Compose validation. Without projects or Compose, those application commands are not applicable, not passed. Review untracked contents directly. After later stages add code, run the verifier without `-BootstrapOnly` and execute the full applicable code validation locally and in CI.

Perform the [adversarial review](docs/engineering/CODE_REVIEW_RULES.md) and use the [unchecked compliance checklist](docs/engineering/AGENT_COMPLIANCE_CHECKLIST.md). Fix relevant failures; record proven blockers and limitations honestly. No automated checker replaces engineering review.

## Security and history

Follow [SECURITY.md](SECURITY.md): no real financial/customer data, real provider secrets, production credentials, or private keys. Use obvious placeholders in [.env.example](.env.example), never credential-bearing `.env` files.

Provide useful, focused Conventional Commit history explaining why, with tests and impact. Agents provide the detailed [commit proposal](docs/engineering/COMMIT_RULES.md) but do not stage, commit, push, tag, create branches, or release without explicit authorization. Stop when the requested stage is complete.
