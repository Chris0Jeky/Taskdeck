# Streaming export history: bounded consumer integration

Date: 2026-09-20. Owner issue: #1399. Parent primitive: #3282. Consumer PR: #3284.

## Decision

Use one ordered extraction-history iterator per existing 500-artefact metadata page. The repository materializes at most 50 payload entities per SQLite page before yielding. The consumer retains one lookahead row and writes each artefact's histories in place after its incremental Base64 blob copy. It does not collect a history dictionary or hold a database reader across destination backpressure.

The iterator is scoped with `await using`: completion, destination failure, missing blobs and cancellation all dispose it. Unexpected leftover history after the metadata page is treated as failure rather than silently omitted. The caller continues to own the destination stream. Cancellation still propagates; failed output does not record a successful export audit.

## Observed negative control

Before the consumer change, head `244a1628ca2a6a2261be206a4acd8c85f399e357` was tested by CI run `35483806206`, Ubuntu API job `106006385880`, synthetic merge `b568b21d51ce278a998338a0653ded1cde819876`.

The full API result was **3329 passed, 4 failed, 4 skipped**. All four failures were the intended SQL-count assertions, after valid JSON, ownership and artefact-tail byte comparisons had passed:

| Fixture | Observed old history SELECTs | Bounded-window target |
| --- | ---: | ---: |
| 12 artefacts, one history each | 12 | 1 |
| 501 artefacts, one history each | 501 | 12 |
| 12 artefacts, including one 123-row history | 14 | 3 |
| 12 artefacts, no histories | 12 | 1 |

These are executed SQLite command counts, not latency measurements or an estimate of end-to-end speedup. The four skips belong to main's quota quarantine, addressed separately by #3280.

## Final coverage and verification procedure

`StreamingExportHistoryConsumerTests` drives the complete export service with real SQLite history and blob repositories. Its independent byte oracle uses the old sequential history reader. It preserves the old raw Base64 token spelling as well as decoded bytes, including `+`, `/`, padding and non-ASCII source text. The envelope's generated export timestamp is intentionally outside tail-byte parity.

Fixtures include the exact 50-row boundary, the 500/501 metadata-page boundary, long histories, sparse and empty histories, and a foreign owner's artefact. Exit-path tests observe real materialization and queries, iterator disposal, cancellation propagation, destination ownership, no false success audit, and reuse of the history/blob connection after exit. Assertions are outside the export catch-all so an assertion failure cannot masquerade as an expected output failure.

Run the focused API class, the existing DataExportService Application tests, and the complete required Linux/Windows matrix at the final source head. The implementation commit has not yet been compiled at the time this note is written; final results belong to the PR's exact-head evidence record. No local .NET run is claimed because the SDK is unavailable in this environment.

## Boundaries

- This changes the streaming artefact-history consumer only. Buffered export, schema, authorization, HTTP routing, JSON field order and blob-copy semantics are unchanged.
- The bound is on materialized history payload rows per repository page, not whole-process RSS, metadata scanning cost or a whole-export transaction snapshot.
- Proposal, chat and audit sections still have separately tracked whole-result reads. #2237 already owns that measurement and paging follow-up; this work does not claim to bound the complete export.
- Tests directly execute the complete service output. They are not a new HTTP transport or production-load qualification; existing API portability tests remain part of the required suite.
- Merge and reconcile #3282 first, then retarget/requalify #3284 against current main. No review or operational acceptance is inferred from the initial red run.
