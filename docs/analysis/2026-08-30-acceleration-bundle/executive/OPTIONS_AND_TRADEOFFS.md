# Options and trade-offs

## Capture migration

**Recommended:** expand → backfill → dual-read shadow comparison → read cutover → stop legacy mutation → later contract. It costs extra temporary code but gives rollback and parity evidence. A one-shot table rewrite is shorter and substantially harder to diagnose or reverse.

## Processing queue

**Recommended:** lease token + expiry + attempt-scoped `ProcessingRun`, with idempotency enforced at the job/output boundary. A database lock around the whole run is simpler but unsuitable for process crashes and long-running model calls.

## Worker transport

**Recommended for v1-alpha:** supervised stdio JSON-RPC plus a private spool directory/content handle. It is easy to contain and inspect. HTTP loopback adds port/auth/firewall complexity without clear value for a child process. Remote HTTP can implement the same semantic protocol later.

## Capability option validation

Start with a deliberately small JSON-Schema implementation boundary. Using a mature library is justified only when manifests need composition/ref resolution; otherwise a constrained internal validator is easier to audit. Record the dependency decision before broad schema features appear.

## SQLite blob streaming

Three credible choices:

1. **SQLite incremental BLOB I/O:** preserves one-file storage and true streaming, but EF Core integration is lower-level.
2. **Bounded chunk rows:** portable and testable, but increases row/index overhead and complicates dedupe/reassembly.
3. **Controlled spool then store:** simplest transition, but peak disk doubles during intake and final insertion may still allocate.

Recommendation: benchmark incremental BLOB I/O first; keep spool-then-store as a bounded fallback, never `MemoryStream` + `byte[]` disguised as streaming.

## Hierarchy materialisation

Use an adjacency list (`ParentCardId`) plus server-side depth/cycle validation for depth ≤3. A closure table is unnecessary at this depth and makes migration/deletion more complex.

## Typed links

Store canonical directed edges for directional types. For symmetric `relates-to`, canonicalise endpoint order or enforce a computed uniqueness key so `A↔B` and `B↔A` cannot coexist.

## Custom-field values

Use typed nullable columns with a database check constraint enforcing exactly one value column per row. A single JSON value is easier initially but weakens indexing, validation and migration clarity.

## SSE fallback

Only retry non-streaming when the server rejects streaming/`response_format` **before any user-visible delta**. Mid-stream fallback would duplicate or contradict output and must surface as a partial failure.

## Telemetry

The issue has already selected opt-in, off-by-default telemetry. Keep the payload first-party, content-free, versioned, inspectable and independently disableable. The strongest trust posture remains a local status command plus public schema/policy, not merely a settings toggle.

## Hosted beta

Do not equate “single shared deployment” with multi-tenant safety. The release gate must prove owner scoping across storage, realtime, workers, exports, logs, caches, quotas and deletion. Public registration should be the last gate, not the first deployment step.

## Smart CI

Prefer an evidence planner that explains both selected and skipped checks. Pure path filters are fast but cannot safely reason about generated code, migrations, security boundaries, flaky lanes or historical failure yield.
