# Definition of Done

Authority: [AGENTS.md](../../AGENTS.md), FL-RULE-020 and FL-RULE-021. Supporting policies: [TESTING_RULES.md](TESTING_RULES.md), [CODE_REVIEW_RULES.md](CODE_REVIEW_RULES.md), and [COMMIT_RULES.md](COMMIT_RULES.md).

"Works on my machine" is not Definition of Done. A task is complete only when every **applicable** condition below has evidence. An inapplicable item needs a reason. A blocked check remains unverified; it must not be checked off as passed.

## Completion conditions

- [ ] Scope understood and all applicable instructions read.
- [ ] Existing code/files and working-tree state inspected; user work preserved.
- [ ] Requested implementation complete, with no unrelated changes or speculative architecture.
- [ ] Domain/data invariants preserved; applicable rule IDs identified.
- [ ] Deterministic tests added or updated for changed behavior.
- [ ] Relevant failures, ambiguity, replay, and out-of-order cases tested.
- [ ] Concurrency considered and proven under real contention where claimed.
- [ ] Multi-instance and restart/crash behavior considered and tested where claimed.
- [ ] Documentation updated and limited to actual implementation plus evidence.
- [ ] Restore/build pass where projects exist.
- [ ] Required tests pass against real persistence where applicable.
- [ ] Format verification passes where supported by existing project tooling.
- [ ] Relevant container configuration validates where it exists.
- [ ] Complete diff, staged changes, and untracked contents reviewed adversarially.
- [ ] No secret, real financial/customer data, or user-specific IDE content added.
- [ ] Known limitations and proven environmental blockers documented.
- [ ] Detailed commit proposal produced; no unauthorized staging/commit/publication.

These boxes are a template, not statements about the current repository or future work.

## Current governance-stage validation

From the repository root on Windows PowerShell 5.1 or later:

```powershell
powershell -NoProfile -File scripts/Verify-Governance.ps1 -BootstrapOnly
dotnet --version
dotnet msbuild Directory.Build.props -nologo -getProperty:TargetFramework,Nullable,ImplicitUsings,TreatWarningsAsErrors,EnableNETAnalyzers,AnalysisLevel,EnforceCodeStyleInBuild,Deterministic
dotnet msbuild Directory.Packages.props -nologo -getProperty:ManagePackageVersionsCentrally,CentralPackageVersionOverrideEnabled
git diff --check
git diff --cached --check
git diff --stat
git status --short --untracked-files=all
```

On a platform with PowerShell 7, use `pwsh` in place of the `powershell` executable. Do not require or install PowerShell 7 merely to run this bootstrap on Windows. Git and the SDK selected by `global.json` are prerequisites for the listed checks.

The verifier checks required governance files, UTF-8/LF/final-newline/trailing-whitespace quality, local documentation links, rule IDs/severities, strict XML defaults, SDK pin structure, selected Git/Docker hygiene, and obvious example settings. `-BootstrapOnly` additionally rejects project/application/service scaffolding and package declarations in the governance-only stage. It neither builds an application nor proves semantic policy compliance, exhaustive secret absence, or security.

MSBuild property evaluation validates the checked-in property values without creating a project. It does not execute compilation, analyzers, restore, tests, or format verification. A pinned SDK and `Deterministic=true` alone are not proof of an end-to-end reproducible application build.

At this stage, application restore/build/test/format and `docker compose config` are **not applicable**, because there is no solution/project or Compose file. Do not run them against a nonexistent target and call that a product defect. Do not create one just to make those checks applicable. Inspect every created file directly because untracked contents are absent from ordinary diff.

## Future code-stage validation

Once a task explicitly creates the relevant solution/project, run these commands with its actual checked-in path (or from a directory with an unambiguous target):

```text
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
docker compose config
git diff --check
git diff --cached --check
git diff --stat
git status --short
```

`docker compose config` applies only when Compose configuration exists. Check all relevant solution/project targets rather than accidentally selecting one of several. Keep local and CI commands equivalent, including configuration, test prerequisites, and the pinned SDK. No pipeline-only setup or silently skipped PostgreSQL tests.

Continue running the governance verifier **without** `-BootstrapOnly` after later stages introduce application files. Do not weaken its checks to make an unrelated implementation pass; policy amendments require explicit authorization.

## Failure handling and evidence report

If a relevant check fails: investigate, fix the cause within scope, and rerun. Do not finish with a known relevant failure unless a proven environmental blocker prevents resolution. Record the exact command, error, attempted diagnosis, impact, and claims left unverified. Environmental failure is not implementation evidence or a passing Definition of Done.

The closing report distinguishes files changed, behavior demonstrated, validation results, inapplicable checks, known limitations, and out-of-scope findings. Include the detailed commit proposal. Stop at the requested stage; do not continue into the next architecture or feature prompt.
