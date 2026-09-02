# Master test matrix

## Context Fabric

| Capability | Domain/unit | Repository/SQLite | API/application | Migration | Export/delete/import | Security/adversarial | End-to-end |
|---|---|---|---|---|---|---|---|
| CF-01 Capture backfill/read switch | mapper/state axes | store CRUD/parity | Inbox byte-compat | forward/resume/down | full round-trip | malformed payload/content-free receipt | seeded legacy DB → native Inbox |
| CF-02 dimensions | mapping/exhaustive enums | persisted columns | old/new DTO conflict | defaults | dimensions round-trip | producer/principal server-stamp | legacy and native clients |
| CF-03 jobs/runs | state/lease/idempotency | atomic claim/renew | runner outcomes | schema/down | run/job export/delete | lease theft/cost/deadline | crash → re-lease → one output |
| CF-04 protocol | validators/handshake | registry persistence where any | host result mapping | n/a | manifest/config | bad processor/network/stderr/kill | PdfPig and mock conformance |
| #1429 containment | limit mapping | warning/run write | host remains ready | n/a | n/a | bomb/process escape/spool | Windows Job + Linux cgroup proof |
| CF-05 semantic.extract | adapter/chunk merge | job inputs | golden path unchanged | predicate retirement | provenance | quota/kill switch | transcript/PDF/plain text same path |
| CF-06 representations | parent XOR/quality | header/payload façade | bounded lineage read | orphan/backfill/down | headers/payloads | cross-owner/supersession | runner creates typed output |
| CF-07 anchors | kind-field matrix | owner-scoped query | stable errors/viewer | span parity/down | anchors survive | guessed ID/cross-owner | legacy text highlight unchanged |
| CF-23 blobs | hash/limits/ref rules | real streaming/dedupe/txn | stable quota errors | artefact migration | large export/delete | foreign release/collision | >buffer upload + two-ref release |

## Work model

| Capability | Domain | API/proposal | Migration | Export/import | UI | Concurrency/security |
|---|---|---|---|---|---|---|
| Type/hierarchy | cycle/depth/scope/subtree | preview/apply same errors | Task default/down | staged parents | picker/tree/cascade preview | reparent conflict, no silent cascade |
| Typed links | canonicalization/cycles | direct/proposal parity | edge uniqueness/down | endpoint remap | inverse labels | duplicate race, no access grant |
| Assignments/estimate | eligible set/arithmetic | actor/audit | nullable defaults | participants before links | picker/units/labels | assignment ≠ access, add/remove race |
| Custom fields | per-type table | definition/value permissions | additive/down | definitions/options before values | generated editors | retired/edit race, unsafe URL |

## Hosted beta

| Domain | Required proof |
|---|---|
| Registration | close switch, verification, rate limit, invite replay |
| Auth/secrets | TOTP/connector encryption, key rotation, wrong-key verifier |
| Isolation | two-user matrix over API/MCP/SignalR/export/blob/evidence/search/diagnostics |
| Abuse/cost | body/file/job/storage/token quotas, global kill switch, queue fairness |
| Backup/restore | checksum, off-host custody, exact image, app-level reads, measured RPO/RTO |
| Operations | upgrade/rollback, status, incident, operator recovery, 7-day dogfood |
| Telemetry | off-by-default, allowlist, zero unexpected egress, retention/public dictionary |

## SSE

- parser chunk-boundary Cartesian cases;
- fake handler that delays chunks and proves first downstream emit precedes response completion;
- cancellation closes HTTP body and controller stream;
- explicit unsupported-stream fallback once;
- no retry after partial output;
- resilience/egress/quota path applies identically.

## Smart CI

- shard inventory exact partition;
- readiness/port/process teardown fixtures on Linux and Windows;
- receipt schema valid and missing receipt rollout;
- critical-path/cost/duplicate-SHA calculations;
- quiet-night and affected-night selection;
- exact-tag clean rebuild and digest/provenance proof;
- rerun-green remains flaky;
- quarantine expiry/owner/issue/wildcard bounds.
