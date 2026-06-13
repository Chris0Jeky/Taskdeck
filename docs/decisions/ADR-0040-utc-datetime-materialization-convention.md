# ADR-0040: Global UTC DateTime Materialization Convention for SQLite

- Status: Accepted
- Date: 2026-06-13
- Deciders: Repository maintainers
- Related: #1191 (DateTime/DateTimeOffset mismatch on `AutomationProposal.ExpiresAt`), ADR-0023 (SQLite-to-PostgreSQL Migration Strategy)

## Context

Taskdeck persists with SQLite via EF Core. SQLite has no native timestamp type: EF Core stores
`DateTime` values as ISO-8601 TEXT **without** timezone information. On materialization, EF Core
reads those TEXT columns back as `DateTime` with `DateTimeKind.Unspecified`.

`AutomationProposal.ExpiresAt` is a `DateTime` written with `DateTime.UtcNow` and compared against
`DateTime.UtcNow` (and, via callers, against `DateTimeOffset` instants) to decide whether a proposal
has expired. When the materialized value is `Unspecified`, a `DateTime` -> `DateTimeOffset` conversion
(`new DateTimeOffset(unspecified)`) interprets the wall-clock numbers in the **host's local zone**,
not UTC. On a host east of UTC (positive offset), the converted instant is *earlier* than the true
UTC instant, so a proposal that is genuinely still valid can compare as already expired -- the #1191
symptom. The drift is exactly the host's local UTC offset, so it is invisible on a UTC host and on
any test that compares ticks (ticks ignore `DateTimeKind`).

This is a cross-cutting persistence convention affecting every `DateTime`/`DateTime?` property in the
model and the contract every writer must honor, which per `CLAUDE.md` warrants an ADR.

## Decision

Apply a **global** UTC normalization convention in `TaskdeckDbContext.ConfigureConventions` using two
`ValueConverter`s registered for all `DateTime` and `DateTime?` properties:

- **Read side (materialization):** every `DateTime`/`DateTime?` read from SQLite is stamped
  `DateTimeKind.Utc` via `DateTime.SpecifyKind(v, DateTimeKind.Utc)`. The bytes EF Core read already
  represent a UTC instant (writers supply UTC); this only restores the lost `Kind`, making subsequent
  `DateTimeOffset` conversions and `DateTime.UtcNow` comparisons correct.

- **Write side (normalization):** a value whose `Kind` is `DateTimeKind.Local` is converted with
  `ToUniversalTime()` before storage; `Utc` and `Unspecified` values pass through untouched. This is
  behavior-preserving for all current writers -- every one of them supplies `DateTime.UtcNow` (Utc
  kind) -- and it closes a latent trap: a future `Local`-kind writer (e.g. `System.Text.Json` binding
  an offset-bearing payload) would otherwise be stored as local wall-time and re-read stamped Utc,
  which is silently wrong.

**Writer contract:** supply UTC (use `DateTime.UtcNow`). `Local` values are normalized on write;
`Unspecified` values are assumed to already be UTC and stored as-is.

### Raw-SQL paths that bypass the convention

EF Core value-conversion conventions apply only to LINQ-tracked column reads/writes. Two repositories
hand-format timestamps into raw SQL and therefore **bypass** the convention -- they must continue to
format UTC explicitly:

- `AuditLogRepository` -- formats range/cutoff bounds as `"yyyy-MM-dd HH:mm:ss.fffffff" + "+00:00"`
  from `.UtcDateTime` for `FromSqlInterpolated` / `ExecuteSqlRawAsync` queries.
- `OAuthAuthCodeRepository` -- formats `now`/`cutoff` as `"yyyy-MM-dd HH:mm:ss.fffffff+00:00"` for
  `ExecuteSqlRawAsync` consume/cleanup statements.

These already emit UTC with an explicit `+00:00` offset, so they are consistent with the convention;
this ADR records them so a future edit does not assume the convention covers raw SQL.

## Alternatives Considered

- **Migrate `ExpiresAt` (and peers) to `DateTimeOffset`.** Rejected. `DateTimeOffset` carries its
  offset and would materialize correctly, but **EF Core's SQLite provider cannot translate
  `DateTimeOffset` comparisons in LINQ** to SQL -- predicates like `p.ExpiresAt < now` would either
  fail to translate or silently evaluate client-side, breaking the housekeeping/expiry queries and
  forcing full-table client evaluation. It is also a schema/data migration with broad blast radius.

- **Per-entity converters on `AutomationProposalConfiguration` only.** Rejected (this was the PR's
  first iteration). It fixes the one reported property but leaves every other `DateTime` column
  (`ArchiveItem.ArchivedAt`, `CommandRun.StartedAt/CompletedAt`, `CommandRunLog.Timestamp`, etc.)
  with the same latent `Unspecified` defect. A global convention fixes the class of bug once.

- **Store an offset suffix in the TEXT column.** Rejected: requires per-property mapping configuration
  and a migration, and still does not give callers a correctly-kinded `DateTime` without a converter.

## Consequences

- Every `DateTime`/`DateTime?` materialized from SQLite now carries `DateTimeKind.Utc`; conversions to
  `DateTimeOffset` and comparisons with `DateTime.UtcNow` are host-timezone-independent.
- The write-side normalization makes the writer contract explicit and removes the `Local`-kind trap.
  No current writer changes behavior (all use `DateTime.UtcNow`).
- The convention does **not** reach raw-SQL paths; `AuditLogRepository` and `OAuthAuthCodeRepository`
  remain responsible for hand-formatting UTC (documented above).
- `DateTimeOffset` properties (`Entity.CreatedAt/UpdatedAt`, `ApiKey.ExpiresAt`, `OAuthAuthCode.ExpiresAt`,
  `ProposalOutcome.DecidedAt`, etc.) are unaffected -- they already carry offset information and
  materialize correctly.
- A regression test (`UtcDateTimeMaterializationIntegrationTests`) round-trips an `AutomationProposal`
  through real SQLite and asserts `DateTimeKind.Utc` on `ExpiresAt` and `DecidedAt`, the `DateTimeOffset`
  comparison behavior, and the `Local`-write normalization. Removing the convention makes it fail.

## References

- #1191 -- DateTime/DateTimeOffset mismatch on `AutomationProposal.ExpiresAt`
- ADR-0023 -- SQLite-to-PostgreSQL Migration Strategy (a future Postgres backend has native
  `timestamptz`; this convention is the SQLite-specific bridge until then)
- `backend/src/Taskdeck.Infrastructure/Persistence/TaskdeckDbContext.cs` -- `ConfigureConventions`
- `backend/tests/Taskdeck.Api.Tests/UtcDateTimeMaterializationIntegrationTests.cs` -- regression test
