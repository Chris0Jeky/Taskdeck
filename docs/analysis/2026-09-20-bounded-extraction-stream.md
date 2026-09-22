# Bounded extraction-history stream (#1399)

## Scope and consumer contract

`IArtefactExtractionRepository.StreamByArtefactsForUserAsync` is a persistence primitive for a later streaming-export consumer switch. Existing `GetByArtefactsForUserAsync` and `DataExportService` are unchanged. This slice does not close the export's N+1 issue or claim actual export-byte parity.

The call snapshots at most 900 raw artefact IDs immediately, rejects oversized input before de-duplication, preserves first-occurrence order, and binds ownership in SQL. Missing and foreign-owned histories yield no rows. SQLite preserves each artefact's stored `CreatedAt, Id` TEXT order; it does not re-sort GUIDs in .NET or normalise the continuation timestamp. Cancellation supplied to the method or async enumerator is honoured. Consume within the repository's scoped lifetime; do not invoke concurrent operations on its DbContext. Sequential context use while the stream is paused is supported because each page finishes reading before yielding.

## Boundedness and trade-offs

A SQLite CTE assigns caller ordinals; a `selected AS MATERIALIZED` CTE selects no more than 50 metadata keys under the owner join and strict ordinal/timestamp/id keyset. Only those keys are joined back to large text payloads. Every page materialises at most 50 untracked extraction entities. Each ID and cursor value is parameterised, with at most 904 parameters. No growing OFFSET, open reader, newly owned transaction, retry loop or all-history dictionary is retained while the destination applies backpressure. Other providers deliberately reuse existing bounded per-artefact pages and native ordering; no batched query-count improvement is claimed there.

A row bound is not a whole-process RSS ceiling: extracted text is UTF-16 in memory, entities carry metadata, JSON can expand characters, the caller can retain rows, and SQLite may scan/sort metadata while selecting a page. The narrow selection prevents sorting an unbounded set of text payloads; it is not a universal CPU or latency bound. There is no whole-export transaction snapshot. Concurrent mutation semantics are not upgraded by batching.

For a fixed non-empty ID window containing H history rows, the SQLite reader performs `floor(H / 50) + 1` payload queries (a final full page needs an empty exhaustion read). Empty ID input performs none; up to 900 IDs with no history require one query. Caller windows should follow existing export page boundaries rather than increasing the 900-ID cap.

## Evidence and tests

The initial draft head `493ff33a7fa62aef284ab6e2ac4efdb6d58ad837` stages a test-only naive adapter that eagerly calls the full-history batch and then yields rows. Its interceptor assertion should reject 123 materialised entities before the first yield. The final source removes that adapter. Observed negative-control and final Linux/Windows results belong to the PR; a compiler error is not a behavioural negative control.

Permanent integration tests compare ordered entity serialization against the existing sequential reader across 126 owned rows, duplicate IDs, empty/foreign histories, timestamp ties, non-zero offsets, sub-millisecond precision, quotes and Unicode. Separate tests count actual EF materialisation at the first yield and page transition, count SQL reader commands, check closed connections and sequential scoped-context reuse, early disposal, exact 50-row boundaries, the 900-ID parameter path, input snapshotting, oversized raw duplicates, and method/enumerator cancellation.

A local Python SQLite 3.46.1 experiment of the exact SQL shape produced pages 50/50/26 matching sequential reads, accepted all 904 bound parameters, and showed `MATERIALIZE selected` before the payload PK lookup in EXPLAIN QUERY PLAN. That is supplementary SQL-shape evidence, not .NET provider or application validation. Local .NET is unavailable; compiled integration evidence comes from exact-head GitHub Actions.

## Next independently reviewable consumer slice

Change `WriteArtefactsTailAsync` to open one bounded stream per existing source-artefact ID page, carry a single lookahead row between artefacts, emit empty history arrays when appropriate, and dispose the stream on cancellation or destination failure. Preserve buffered export behaviour. Before that switch is merge-ready, compare actual old/new export bytes, assert real endpoint SQL-count reduction for many artefacts, and cover output failure, blob-copy interleaving, and a history larger than one page. Do not mark #1399 closed on the strength of the primitive alone.
