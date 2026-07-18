# ADR-0048: Decompression-Bomb Containment Boundary — Memory-Capped Extraction Worker Process

- Status: Accepted
- Date: 2026-07-18
- Deciders: Overnight coordinator, on the containment-boundary choice the maintainer explicitly delegated in the 2026-07-18 launch directive; the reversal below remains open to the maintainer
- Related: ADR-0047 (artefact-extraction resource bounding — permit gate + provider-injection depth; this ADR is its deferred containment boundary), ADR-0046 (generalist artefact intake), ADR-0044 (revival pivot / finite-work discipline), `#1379` (extraction resource ceiling — stays open for this work), PR `#1417` stop-gate adjudication (comment `5007117851`)

## Context

Artefact text extraction must survive a decompression bomb — a PDF whose compressed streams (a few KiB) inflate to gigabytes during decode, exhausting host memory while staying under every input-byte cap.

The `#1379` attempt to contain this in-process, by injecting a decoded-byte ceiling through PdfPig's `ParsingOptions.FilterProvider` (`BoundedFilterProvider`), was proven insufficient (PR #1417 stop-gate, comment `5007117851`, established two independent ways):

- **Object streams (`/ObjStm`) are covered** by the injected provider.
- **Cross-reference streams are NOT.** PdfPig 0.1.15's `XrefStreamParser.TryReadStreamAtOffset` decodes with a **hard-coded `DefaultFilterProvider.Instance`**, invoked from `FirstPassParser.Parse` **before** the wrapped provider is ever constructed. The injected ceiling structurally cannot reach that decode, so a single-filter FlateDecode xref stream inflates unbounded.

No amount of filter cleverness closes a path the injection point cannot see. A hard, path-independent guarantee therefore cannot be delivered in-process on the pinned parser. Because the extraction service has no production caller yet (this is the wiring prerequisite), there is no current exposure — this decision fixes the boundary before wiring, not in response to an incident.

## Decision

**The containment boundary for decompression bombs is a process-level memory cap on a dedicated extraction worker process.** A parse that exceeds the cap is killed by the OS/runtime and reported as a content-free warning outcome (the same shape as the existing `extraction-timeout` row), never a host-wide OOM.

- **Per-platform cap mechanism:** a Windows **Job Object** with `JOB_OBJECT_LIMIT_JOB_MEMORY` for the desktop/native run path; a **container/cgroup memory limit** for the Docker run path. The cap governs the whole worker process, which hosts exactly one parse at a time, so "process cap" equals "per-parse cap" by construction — covering xref streams and every other path the in-process ceiling cannot see.
- **Provider-injection ceiling (ADR-0047 §2) is retained as defense-in-depth** for the paths it provably covers (ObjStm), giving an in-process early abort with a precise warning before the coarse process cap fires. It is not the boundary.
- **The permit gate (ADR-0047 §1) is independent and already shipped.** It bounds concurrency and abandoned-thread accumulation; it composes with, but does not depend on, this boundary.

The kill/timeout semantics reuse the existing abandonment vocabulary: a killed worker yields one immutable warning-bearing history row, no HTTP 500, artefact stays stored. Configuration keys for the worker path ship **default-OFF** until the mechanism is proven on both run paths.

## Alternatives Considered

- **(a) Raw-bytes bounded pre-inflate scan before `PdfDocument.Open`.** REJECTED as the primary boundary. To bound decode you must locate every stream and its filter chain, which means tokenizing PDF structure ahead of the real parser — a second, partial PDF parser. It is blocklist-shaped (enumerate known bomb vectors) and defeatable by tokenizer evasion (object-stream indirection, malformed-but-tolerated structure, filter aliasing) where the pre-scan and PdfPig disagree on what a stream is. Retained at most as an optional cheap in-process pre-filter, never as the guarantee.
- **(c) Upstream PdfPig fix/fork** threading `ParsingOptions.FilterProvider` through `FirstPassParser`/`XrefStreamParser`. This is the cleanest long-term exit and is **pursued as the long-term fix**, but it is externally timed (upstream release cadence) and cannot gate this work. **The public disclosure/upstream-filing step is HUMAN-OWNED** — no upstream issue or PR is filed by an agent.
- **In-process only (accept the xref gap).** Rejected: it would ship a known-unbounded memory path as if contained, exactly the overclaim ADR-0047 corrects.
- **OS `ulimit`-style whole-host limit.** Rejected: coarse and host-wide — it cannot distinguish one bomb from legitimate concurrent load and would take the host down rather than record an honest per-artefact warning. A per-worker Job Object / cgroup scopes the cap to the single parse.

## Consequences

- **This explicitly REVERSES the earlier rejection of process isolation** (the unmerged ADR-0047 draft rejected an out-of-process parse "sidecar" as disproportionate for a local-first single-file app). The reversal is on **new evidence**: the hard-coded `DefaultFilterProvider.Instance` proof showed the in-process bound the draft relied on does not hold, removing the premise that made the sidecar unnecessary. Stating the reversal openly per ADR discipline. **The reversal remains open to the maintainer** — if the maintainer prefers to accept the xref gap or wait for the upstream fix instead, this ADR is revised.
- **Cost:** a process host (spawn, lifecycle/health/restart supervision, IPC contract for artefact bytes in and extraction result/warnings out) and cross-platform cap wiring across the desktop-exe and container run paths — the standing operational surface the draft wanted to avoid, now justified by the proven gap.
- **Spawn latency:** each extraction pays worker start-up (or warm-pool checkout) cost; acceptable because extraction is already off the interactive path and rate-bounded by the permit gate.
- **True hard memory guarantee:** the OS enforces the cap regardless of what the parser does internally, so a bomb on any path (xref included) is contained rather than merely detected.
- **Delivery:** implementation is tracked as a `#1379` follow-up (see the seeded implementation issue). Config keys ship default-OFF; the acceptance bar includes a bomb-fixture test proving the cap kills the worker on both the Job Object and cgroup paths. The provider ceiling (ADR-0047 §2) and this boundary land together; the permit gate (ADR-0047 §1) is already independently merged.
- **Ratification:** ships **Accepted** on the coordinator's delegated authority; the reversal and the boundary choice are surfaced for maintainer confirmation.

## References

- PR `#1417` stop-gate adjudication — comment `5007117851` (the `DefaultFilterProvider.Instance` proof; ObjStm-covered / xref-gapped)
- ADR-0047 — permit gate (shipped) + provider-injection ceiling (depth); this ADR is its deferred containment boundary
- Issue `#1379` — extraction resource ceiling (stays open); the memory-capped-worker implementation issue links here
- PdfPig 0.1.15 `XrefStreamParser.TryReadStreamAtOffset`, `FirstPassParser.Parse`, `DefaultFilterProvider.Instance`
- Windows Job Objects (`JOB_OBJECT_LIMIT_JOB_MEMORY`); Linux cgroup / container memory limits
