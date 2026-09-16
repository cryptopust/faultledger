# ADR 0002: Transfer domain and PostgreSQL persistence

## Context

Stage 1 needs a synthetic successful submission slice, exact money, explicit
state transitions, and real migrated PostgreSQL persistence. The policies in
[AGENTS.md](../../AGENTS.md) remain unchanged. No real provider is connected.

## Decisions

Money accepts signed decimal values bounded by 20 integer and 8 fractional
digits, matching `numeric(28,8)`. Currency is exactly three ASCII letters,
canonicalized to uppercase invariantly; this is shape validation, not an ISO
registry. Input precision is rejected, never rounded. Transfer amounts must
be positive. JSON input uses fixed-point numeric tokens, validated before
decimal conversion; exponent notation and quoted numbers are rejected.

Domain state changes are semantic operations. PostgreSQL is authoritative.
Infrastructure maps private persistence records into validated domain objects;
neither Domain nor Application references EF, Npgsql, or ASP.NET Core. A small
transfer-specific store contract expresses the persistence boundary without a
generic repository or UnitOfWork abstraction.

An explicit bigint version counts domain transitions. EF uses the original
version in update predicates and reports conflicts without retries. Each state
save commits independently. Created, ReadyToSubmit and Submitting are durable
before the provider is invoked. No transaction spans external I/O. TimeProvider
is read in Application; Domain canonicalizes supplied timestamps to UTC
microseconds to match PostgreSQL timestamp resolution.

UUIDs are generated at the application boundary; tests supply fixed identities
where assertions depend on them. Synthetic provider references derive from
the transfer UUID and never select failure behavior.

## Scope conflict and safe restriction

FL-RULE-006 ultimately requires same-request recovery, but this task expressly
defers the replay/fingerprint algorithm. Stage 1 implements only a fail-closed
subset: globally unique, case-sensitive idempotency keys and an explicit 409
for every duplicate, before another provider call. It does not claim full
idempotency compliance. No existing operation is resumed or reposted. No
fingerprint, retention/deletion policy, or high-contention algorithm is added.
Stage 3 must implement canonical same-request recovery and conflicting-request
semantics; the policy is not weakened to conceal this limitation.

## Dependency decision (FL-RULE-017)

Problem: Stage 1 explicitly requires EF Core persistence, concurrency mapping,
and generated migrations for PostgreSQL.

Why BCL/framework capability is insufficient: the BCL and ASP.NET shared
framework do not contain EF's mapper, change tracking, migration generator,
or PostgreSQL EF provider.

Why existing dependencies are insufficient: Npgsql supplies ADO.NET connectivity,
not EF mappings or migrations. Existing Testcontainers and xUnit test the new
boundary without adding another harness.

Dependencies: Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Relational,
and Microsoft.EntityFrameworkCore.Design 10.0.7; Npgsql.EntityFrameworkCore.PostgreSQL
10.0.3. The existing Npgsql 10.0.3 is retained. EF runtime/relational versions are
aligned explicitly; Design is private tooling. A repository-local dotnet-ef
10.0.7 manifest enables migrations without system-wide installation. These are
stable net10-compatible packages, not preview dependencies. No unrelated SDK,
runtime, package, or image update is included.

Operational cost: lockfile review, EF/Npgsql maintenance, migration review and
explicit deployment, and Docker for real PostgreSQL evidence. EF is MIT licensed;
the Npgsql provider uses the PostgreSQL license. Package restore vulnerability
checks and subsequent dependency review remain required.

Failure modes introduced: mapping/schema drift, migration failure, numeric
coercion, concurrency conflicts and database outages. Guard mappings and inputs,
inspect generated SQL, and exercise actual PostgreSQL tests rather than substitutes.

Removal cost: replace Infrastructure mappings, store implementation, and migration
tooling while preserving the pure Domain/Application contracts and durable data.

## Consequences and alternatives rejected

The explicit version is easier to inspect than xmin and does not couple Domain
to PostgreSQL system columns. Infrastructure records avoid ORM materialization
silently bypassing domain validation at the cost of a small explicit mapping.
No microservices, broker, Redis, generic repositories, retry framework, or audit
table is justified. EF InMemory/SQLite are not PostgreSQL evidence.

`numeric(28,8)` itself coerces excess fractional input before CHECK evaluation.
All supported write paths validate Money first. Direct privileged SQL can still
round an out-of-policy amount; this laboratory does not claim to protect against
arbitrary administrative writes. Currency/state/range consistency constraints
protect persisted representations; future restricted operational roles belong
to a separately scoped security design.

## Known failure window

A crash or cancellation after durable Submitting, including after provider
acceptance but before Accepted is committed, leaves possibly attempted work.
Submitting must be treated as ambiguous dispatch intent, never as permission to
resubmit. Stage 1 has no restart dispatcher, unsafe retry, or automatic repost.
Unknown is a domain state, not implemented outcome detection or reconciliation.
Stage 4 must provide that recovery design. Accepted is not Completed.

## Evidence status

See [transfer-domain.md](../architecture/transfer-domain.md) and the Stage 1
validation record for exact results. Docker-dependent evidence must remain
explicitly blocked if the workstation lacks Docker. A conditionally authorized
local commit is not full Definition of Done.

## Primary references

- [PostgreSQL numeric precision and coercion](https://www.postgresql.org/docs/18/datatype-numeric.html)
- [PostgreSQL timestamp resolution](https://www.postgresql.org/docs/18/datatype-datetime.html)
- [EF application-managed concurrency tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [EF design-time context factory](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation)
- [Npgsql EF 10.0.3 compatibility/dependencies](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3)
- [EF 10.0.7 package/version alignment](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.7)
