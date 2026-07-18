# ADR-0047: Artefact-Extraction Resource Bounding — Permit Gate (Shipped) + Provider-Injection Decode Ceiling as Defense-in-Depth

- Status: Accepted (under authority the maintainer delegated 2026-07-18; open to maintainer revision)
- Date: 2026-07-18
- Deciders: Overnight coordinator, on the boundary choice the maintainer delegated 2026-07-18; maintainer ratifies
- Related: ADR-0048 (decompression-bomb containment boundary — the worker-process decision this defers hard memory containment to), ADR-0046 (generalist artefact intake — the wave this hardens), ADR-0044 (revival pivot / finite-work discipline), `#1379` (extraction resource ceiling), `#1369` (wall-clock extraction budget), PR `#1417` and its stop-gate adjudication (comment `5007117851`), `docs/platform/CONFIGURATION_REFERENCE.md` §Artefacts

> **Supersedes an unmerged draft.** An earlier `ADR-0047` draft ("Bound Artefact-Extraction Resource Consumption In-Process (Permit Gate + Bounded Filter) over Process Isolation", Proposed) exists **only on the unmerged `issue-1379/extraction-resource-ceiling` branch (PR #1417)**. That draft claimed worst-case in-process work is bounded at `ExtractionMaxConcurrency × ExtractionMaxDecodedBytes`. **That claim is proven false for the cross-reference-stream path** (see Context). This ADR takes the same number on `main` (the draft never merged) and records the honest, evidence-corrected decision.

## Context

`ArtefactExtractionService` runs local text extractors (PdfPig for PDFs, plain-text/Markdown) over stored artefact bytes. Slice `#1369` added a wall-clock budget: because PdfPig's `PdfDocument.Open(...)` is synchronous and does not observe a `CancellationToken`, the parse runs on a `Task.Run` worker and a runaway parse is *abandoned* (the request returns an `extraction-timeout` warning row) rather than cancelled.

That left three gaps, all reachable by a crafted PDF under the existing byte/page/character caps:

1. **Abandoned-thread CPU.** An abandoned parse keeps one thread-pool thread at full CPU until PdfPig finishes on its own. Only the request was bounded.
2. **Unbounded accumulation of abandoned parses.** Nothing capped how many such threads could pile up under concurrent parser-bomb submissions.
3. **Memory / decompression ceiling.** A FlateDecode "decompression bomb" — a few KiB of compressed stream that inflates to gigabytes — stays under the 10 MiB input cap while exhausting memory during the decode itself.

The `#1379` work attempted to close all three in-process. Gaps 1 and 2 are cleanly closed by a permit gate. Gap 3 was attempted with a decode ceiling injected via `ParsingOptions.FilterProvider` (`BoundedFilterProvider`). A stop-gate verification on PR #1417 (comment `5007117851`), proven two independent ways (probe tests + PdfPig 0.1.15 source read), established:

- **Object streams (`/ObjStm`) ARE covered** by the injected provider — a bomb inside an object stream trips the decoded-size ceiling.
- **Cross-reference streams (xref streams) are NOT covered.** `XrefStreamParser.TryReadStreamAtOffset` decodes with a hard-coded `DefaultFilterProvider.Instance`, called from `FirstPassParser.Parse` **before** `PdfDocumentFactory.OpenDocument` constructs the wrapped provider. `ParsingOptions.FilterProvider` structurally cannot reach it. A single-filter FlateDecode xref stream is therefore unbounded regardless of how good the injected filter is.

**Consequence:** provider injection cannot deliver a "detect before materialize" containment guarantee on PdfPig 0.1.15. The `cap × ceiling` bound the draft ADR claimed is false for the xref path. The service has no production caller by design (this wiring is the prerequisite for that caller), so there is zero current exposure — this is a correctness-of-record and future-safety decision, not an incident.

## Decision

Split extraction resource bounding into an honestly-scoped mechanism set:

1. **Extraction permit gate (`ArtefactExtractionGate`, Application singleton) — SHIPPED in this PR.** Wraps `SemaphoreSlim(ExtractionMaxConcurrency)` (new setting, `int`, default 2, `[Range(1, 64)]`). The service takes a permit with a non-blocking `Wait(0)` immediately before spawning the parse worker. **Permits track parse-THREAD occupancy, not request lifetime:** released in the `finally` on completed paths, but on abandoned paths (budget timeout or caller cancellation) release is deferred into the worker-completion continuation, so a runaway thread holds its permit until it *actually finishes*. Once permits are exhausted, a new extraction is **rejected pre-parse** with `TooManyRequests` and **no history row** (capacity is a transient property of the box); rejection, not queueing. This closes gaps 1 and 2 and is independent of any containment choice. It is sound under any resolution of gap 3.

2. **Provider-injection decode ceiling (`BoundedFilterProvider`) — DEFENSE-IN-DEPTH, NOT containment; NOT merged here.** Decorates PdfPig's `DefaultFilterProvider` via `ParsingOptions.FilterProvider`, capping cumulative decoded bytes at `ExtractionMaxDecodedBytes`. It **provably covers the object-stream (`/ObjStm`) path** and is retained as a depth layer for the paths it can see. It **does not cover the xref-stream path** (see Context) and must never be relied upon as the containment boundary. It carries open review findings (Flate corrupt-header bypass, LZW under-admission, unguarded RunLength) and is coupled to the containment redesign, so it lands **with** the ADR-0048 worker-process work on `#1379`, not in this permit-only PR. Its ObjStm coverage and the xref gap are pinned by the probe tests that remain on PR #1417 as evidence.

3. **Containment boundary — deferred to ADR-0048.** The only mechanism that hard-bounds a single parse's peak memory across paths the process cannot see (xref streams included) is a process-level memory cap on a dedicated extraction worker. That is decided in ADR-0048.

## Alternatives Considered

- **Keep the draft's in-process `cap × ceiling` as the containment story.** Rejected: proven false for xref streams (Context). Shipping it as containment would be a dishonest guarantee.
- **Raw-bytes bounded pre-inflate before `PdfDocument.Open`.** Rejected as *primary* containment (see ADR-0048): the tokenizer becomes a second PDF parser and is blocklist-shaped / tokenizer-evasion bypassable.
- **Drop the provider ceiling entirely.** Rejected: it provably covers the ObjStm path at low cost and is worth keeping as depth once the real boundary (ADR-0048) exists. Dropping it would discard a working layer.
- **Ship the permit gate and the decode ceiling together now.** Rejected: the decode ceiling has open HIGH findings and depends on the containment redesign; bundling would import unfixed findings into an otherwise-sound permit PR and block the sound half behind the parked half.

## Consequences

- The permit gate ships now: box-wide extraction CPU burn under a bomb flood is capped at `ExtractionMaxConcurrency` spinning threads, and abandoned-parse accumulation is bounded. `TooManyRequests` becomes a retryable outcome the future upload/worker caller must handle. `ExtractionMaxConcurrency` is a new startup-validated `Artefacts` setting.
- **No decode-ceiling code, no `ExtractionMaxDecodedBytes` setting, and no `decoded-size-limit` warning code ship in this PR.** They land with ADR-0048's worker-process work. The `#1379` hard gate (no request/worker wiring until a real memory-containment boundary exists) still stands.
- The record no longer claims a false containment bound. Future contributors reading this ADR learn the xref gap and where the evidence lives (PR #1417, comment `5007117851`) rather than inheriting the overclaim.
- The permit gate is a process-wide singleton, so its bound is per-process; a multi-process deployment bounds each process independently — acceptable at current scale.
- Ratification: ships **Accepted** on the coordinator's delegated authority (maintainer delegated the boundary choice 2026-07-18); the maintainer may revise.

## References

- PR `#1417` — the parked extraction-ceiling work; stop-gate adjudication comment `5007117851` (ObjStm-covered / xref-gap proof); the overclaiming draft `ADR-0047` lives only on its branch
- Issue `#1379` — extraction resource ceiling (stays open for the containment work)
- `#1369` — the wall-clock extraction budget this builds on
- ADR-0048 — decompression-bomb containment boundary (memory-capped worker process)
- ADR-0046 — the generalist artefact-intake wave whose extraction lane this hardens
- PdfPig 0.1.15 `XrefStreamParser.TryReadStreamAtOffset`, `FirstPassParser.Parse`, `DefaultFilterProvider.Instance`, `ParsingOptions.FilterProvider`
- `docs/platform/CONFIGURATION_REFERENCE.md` §Artefacts — `ExtractionMaxConcurrency`
