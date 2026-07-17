# ADR-0047: Bound Artefact-Extraction Resource Consumption In-Process (Permit Gate + Bounded Filter) over Process Isolation

- Status: Proposed
- Date: 2026-07-17
- Deciders: Maintainer (Chris); coordinator design ratified on issue `#1379`
- Related: ADR-0046 (generalist artefact intake — the wave this hardens), ADR-0044 (revival pivot / finite-work discipline), `docs/platform/CONFIGURATION_REFERENCE.md` §Artefacts, issue `#1379`, prior extraction slice `#1369` (wall-clock budget)

## Context

`ArtefactExtractionService` runs local text extractors (PdfPig for PDFs, plain-text/Markdown) over stored artefact bytes. Slice `#1369` added a wall-clock budget: because PdfPig's `PdfDocument.Open(...)` is synchronous and does not observe a `CancellationToken`, the parse runs on a `Task.Run` worker and a runaway parse is *abandoned* (the request returns a `extraction-timeout` warning row) rather than cancelled.

That left three gaps, all reachable by a crafted PDF that stays under the existing byte / page / character caps:

1. **Abandoned-thread CPU.** An abandoned parse keeps one thread-pool thread at full CPU until PdfPig finishes on its own. Only the request was bounded, not the thread.
2. **Unbounded accumulation of abandoned parses.** Nothing capped how many such threads could pile up under concurrent parser-bomb submissions — N bombs meant N spinning threads.
3. **Memory / decompression ceiling.** A FlateDecode "decompression bomb" — a few KiB of compressed stream that inflates to gigabytes — stays under the 10 MiB input cap while exhausting memory during the decode itself.

The service is not yet wired to any request or worker path; this hardening is the prerequisite for that wiring (the `#1379` gate: no request-path or worker wiring lands until these bounds exist). The governing constraint is ADR-0044's finite-work discipline and the local-first thesis: Taskdeck is a single-SQLite-file app a developer runs on their own box.

## Decision

Bound extraction resource consumption **in-process** with two mechanisms plus their configuration, rather than isolating the parser in a separate process. Worst-case in-memory extraction work is thereby bounded and operator-configurable at `ExtractionMaxConcurrency × ExtractionMaxDecodedBytes`.

1. **Extraction permit gate (`ArtefactExtractionGate`, Application singleton).** Wraps `SemaphoreSlim(ExtractionMaxConcurrency)` (new setting, `int`, default 2, `[Range(1, 64)]`). The service takes a permit with a non-blocking `Wait(0)` immediately before spawning the parse worker (uniform for every extractor). **Permits track parse-THREAD occupancy, not request lifetime:** the permit is released in the `finally` on completed paths, but on abandoned paths (budget timeout or caller cancellation) release is deferred into the existing worker-completion continuation — so a runaway thread holds its permit until it *actually finishes*. That single property makes the cap cover concurrently-abandoned parses: once the permits are exhausted, a new extraction is **rejected pre-parse** with `TooManyRequests` and **no extraction-history row** (capacity is a transient property of the box, not of the artefact), spawning zero new threads. Rejection, not queueing: a queue in front of permits held by spinning bombs is just a second unbounded backlog; callers-to-be retry on 429.

2. **Decompression ceiling (`BoundedFilterProvider`, Infrastructure).** Decorates PdfPig's `DefaultFilterProvider`, injected per parse via `ParsingOptions.FilterProvider` (verified present on pinned PdfPig 0.1.15). A cumulative decoded-byte counter, shared across every stream in the document, is capped by `ExtractionMaxDecodedBytes` (new setting, `long`, default 128 MiB, floor 64 KiB). It **never reimplements a filter — it counts, then delegates:**
   - FlateDecode (the practical bomb vector): a cheap streaming counting pre-pass inflates the raw bytes with `ZLibStream` into a fixed 64 KiB discard buffer and aborts the moment the running total would cross the remaining budget — before the real decode materializes anything. Predictors / DecodeParms stay PdfPig's problem.
   - LZW (rare, no cheap .NET counter): conservative admission by worst-case (~1:256) expansion ratio.
   - Every filter: a post-decode backstop adds the actual output length to the cumulative counter, catching multi-stream accumulation and non-Flate growth.
   On breach it sets an authoritative `LimitExceeded` flag and throws an internal `ExtractionDecodedSizeLimitException`; the extractor catches it (around `PdfDocument.Open` and the per-page loop) — and also consults the flag, since PdfPig may wrap or swallow the throw — returning a content-free `decoded-size-limit` warning result. That flows through the normal service path as one warning-bearing history row, never a crash or an `extractor-error` misclassification.

3. **Configuration.** Both settings bind under `Artefacts`, are startup-validated via data annotations (the `int`/`long`-operand `Range` distinction is deliberate — an `int`-operand range would overflow a `long` bound; a `double`-operand range would coerce an `int`), and are documented in `docs/platform/CONFIGURATION_REFERENCE.md`.

## Alternatives Considered

- **Out-of-process parse sidecar (true process isolation).** Rejected as disproportionate for a local-first single-file app. A parse sidecar means a second executable, IPC/serialization of artefact bytes and results, lifecycle/health/restart supervision, and cross-platform packaging across the desktop-exe and container run paths — permanent operational surface to defend against a threat (a hostile PDF the user themselves supplied to their own box) that the in-process `cap × ceiling` bound already contains. The isolation dividend (a crash cannot take down the host) does not justify the standing cost at this trust model and this maturity. Revisit only if extraction is ever exposed to genuinely untrusted multi-tenant input.
- **OS resource limits (cgroups / job objects / `ulimit`).** Rejected: platform-specific, absent on the desktop-exe path, and coarse — they cap the whole process, not one extraction, so they cannot distinguish a bomb from legitimate concurrent load and would take the host down rather than record an honest per-artefact warning.
- **Queue saturated extractions instead of rejecting.** Rejected: a queue in front of permits held by abandoned, still-spinning bombs is a second unbounded set. Rejection with a retryable 429 keeps the bound hard.
- **Kill the abandoned thread.** Not possible: .NET cannot safely abort a thread that never observes cancellation; `Thread.Abort` is unsupported on modern .NET. Bounding *how many* such threads may exist (the permit gate) is the available lever.
- **Reimplement a bounded FlateDecode/LZW decoder.** Rejected: predictors and DecodeParms are subtle and security-relevant; a bespoke decoder is a new bug surface. The decorator counts and delegates, so PdfPig remains the single source of decode truth. (If a needed member had been internal or undelegatable, the slice was to stop and report rather than reimplement — it was not needed; the 0.1.15 filter API delegates cleanly.)
- **Record a history row for saturation rejection.** Rejected: capacity is a transient property of the box at that instant, not of the artefact (unlike the timeout and decoded-size outcomes, which are). A retry when capacity frees should extract normally, so no misleading permanent row is written.

## Consequences

- Worst-case concurrent in-memory extraction work is bounded and operator-tunable at `ExtractionMaxConcurrency × ExtractionMaxDecodedBytes` (defaults: 2 × 128 MiB). Box-wide extraction CPU burn under a bomb flood is capped at `ExtractionMaxConcurrency` spinning threads.
- A new persisted warning code, `decoded-size-limit`, joins the stable extraction-warning vocabulary. Downstream consumers of extraction warnings must treat it as another benign "extraction produced no usable text" signal.
- Legitimate PDFs pay one extra bounded inflate on their FlateDecode streams (the pre-pass). For ≤10 MiB inputs this is negligible; a parity test asserts byte-identical extracted text through the bounded vs. default provider.
- Callers of `IArtefactExtractionService` (none yet; the upload path / worker to come) must handle `TooManyRequests` as retryable. This is recorded now so the wiring slice inherits the contract.
- The permit gate is a process-wide singleton, so its bound is per-process. A future multi-process deployment (Api + Cli + MCP sharing the SQLite file) would bound each process independently — acceptable at current scale; revisit if extraction moves to a shared worker tier.
- Ratification: ships **Proposed**; the maintainer ratifies. The `#1379` hard gate (no request/worker wiring in this slice) stands — this PR is the prerequisite, not the wiring.

## References

- Issue `#1379` — the coordinator-ratified design (comment `5006571640`), verified against code at `21197662`
- `#1369` — the wall-clock extraction budget this builds on
- `docs/platform/CONFIGURATION_REFERENCE.md` §Artefacts — `ExtractionMaxConcurrency`, `ExtractionMaxDecodedBytes`
- ADR-0046 — the generalist artefact-intake wave whose extraction lane this hardens
- PdfPig 0.1.15 `UglyToad.PdfPig.Filters.IFilterProvider` / `IFilter` / `DefaultFilterProvider`; `ParsingOptions.FilterProvider`
