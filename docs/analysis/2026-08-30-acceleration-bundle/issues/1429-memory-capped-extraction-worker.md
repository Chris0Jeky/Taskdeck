# Memory-capped extraction worker process — ADR-0048 containment boundary, first CF-04 sidecar (#1429)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2348 follow-up. Planning input, not authority: the live issue and its four comments, ADR-0047, ADR-0048, ADR-0065 §Decision 10, `docs/architecture/WORKER_PROTOCOL_V1.md` and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

An OS-enforced per-process memory cap around exactly one PDF parse, so a crafted xref-stream
decompression bomb kills a worker instead of the host and produces exactly one content-free
warning-bearing history row. It is delivered **through CF-04's supervisor**, not as a second process
host — and it is the proof that promotes Worker Protocol v1-alpha halfway to v1.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-04 `#2258` | open | **host provider, and mutually gated** | This issue's supervisor *is* CF-04's (issue comment 2026-08-30). CF-04's acceptance box 1 is this issue's containment proof. The cycle resolves by slice, not by issue — see correction 1 |
| CF-03 `#2257` | open | **not** a hard predecessor | The bundle lists it. A contained parse producing one history row needs no job or run; only the CF-04 conformance items that name `ProcessingRun` do |
| `#1379` | open | the gate this unblocks | "Extraction resource ceiling" — wiring the extraction service to any request/worker path is gated on this containment boundary |
| `#1430` | closed | delivered the other half | The permit/concurrency gate (`ArtefactExtractionGate`), ADR-0047 |
| `#1417` | parked | source of MEM-6 | `BoundedFilterProvider` + `ExtractionMaxDecodedBytes` were never merged — grepped, **zero hits** in `backend/`. Its four open review findings (Flate corrupt-header bypass HIGH-1, LZW under-admission HIGH-2, RunLength unguarded MEDIUM-3, 128 MiB default LOW-6) plus the two carry-forwards in the 2026-07-25 comment must be fixed *before* the port lands |
| CF-14 `#2268` (WhisperX) | open (v0.5) | co-condition | v1-alpha becomes v1 only when this **and** WhisperX both pass CF-04's suite |
| CF-23 `#2276` | open | adjacent | Streaming bytes to a spool file without materialising them is exactly the `IBlobStore` streaming requirement |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `MEM-1-launcher-contract` | The launcher abstraction: resource limits, cancellation, exit classification (memory-kill vs deadline vs crash vs protocol-malformed), and a **fake launcher** for deterministic tests. No PdfPig behaviour change | — | contract-only | **Yes — start here**, provided it is written as the internals of CF-04's supervisor (`CF04-b`), not as a parallel host. Exit classification and the fake launcher are pure and need no child process |
| `MEM-2-windows-boundary` | Job Object with `JOB_OBJECT_LIMIT_JOB_MEMORY`, `KILL_ON_JOB_CLOSE`, process-tree assignment before any untrusted work, deterministic teardown tests | 1 | implementation | No |
| `MEM-3-linux-boundary` | cgroup v2 / container memory maximum, pids limit, read-only filesystem except the spool, no network, non-root, kill the whole cgroup | 1 | implementation | Parallel with MEM-2 (disjoint platform code), not before MEM-1 |
| `MEM-4-pdfpig-adapter` | Exactly one parse per worker, bytes delivered by spool handle, exits mapped to stable warning codes | 2, 3, CF-04 `CF04-b`/`CF04-c` | implementation | No |
| `MEM-5-bomb-proof` | Safe synthetic xref-stream fixture, host and worker peak memory recorded, one history row proven, repeated runs leave the host healthy | 4 | implementation | No |
| `MEM-6-depth-layer` | `BoundedFilterProvider` + `ExtractionMaxDecodedBytes` ported from `#1417` **with the four findings and two carry-forwards fixed**, as ObjStm defence-in-depth | — (independent) | implementation | **Yes, technically** — it is in-process and touches no worker. But it is defence in depth, not the boundary: shipping it first risks the "we have a ceiling now" misreading ADR-0047 §2 exists to prevent. Land it after MEM-1, and say in the PR that it is not the boundary |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Permit gate | `ArtefactExtractionGate` (Application singleton, non-blocking `TryAcquire`, saturation ⇒ pre-parse rejection, over-release throws) | **exists** | `Application/Services/ArtefactExtractionGate.cs`. ADR-0047's shipped half (`#1430`) |
| Extraction settings | `ArtefactStorageSettings.MaxBytesPerArtefact`, `MaxBytesPerUser`, `ExtractionTimeoutSeconds`, `ExtractionMaxConcurrency` (default 2, `[1, 64]`, startup-validated) | **exists** | The new worker keys go beside these, under `Artefacts`, default **off** |
| Warning vocabulary | `ArtefactExtractionWarningCodes` — `no-text-layer`, `page-limit`, `character-limit`, `input-too-large`, `invalid-utf8`, `invalid-text`, `extractor-error`, `extractor-contract-error`, `extraction-timeout` | **exists** | The memory-kill outcome needs **a new stable code** (the issue says "define/adopt one"); it must be distinguishable from `extraction-timeout`, because the service already warns that conflating them loses information (`ArtefactExtractionService.cs:132`) |
| Extraction service | `ArtefactExtractionService` (wall-clock bound, abandoned-thread path, one `ArtefactExtraction` per run) | **exists** | `new ArtefactExtraction(...)` at `ArtefactExtractionService.cs:254` is the sole writer |
| **Extraction is unwired** | `IArtefactExtractionService` appears only at `Api/Extensions/ApplicationServiceRegistration.cs:95` | **exists, no production caller** | Grepped across `Taskdeck.Api` and `Application/Services`: registration only. Confirms the v0.3 RC answer q-13 = A (`#1429` comment 2026-08-29) — v0.3 shipped with extraction unwired, and `#1379` is what wiring it needs |
| Extractors | `PlainTextArtefactTextExtractor` (Api DI), `PdfPigArtefactTextExtractor` (Infrastructure DI, version read off the PdfPig assembly at runtime) | **exists** | The registry (CF-04 `CF04-a`) is what selects between them once it lands |
| PdfPig version | `backend/Directory.Packages.props:51` — **0.1.16** | **exists, and the ADRs say 0.1.15** | ADR-0047 line 20/59 and ADR-0048 line 15/53 name 0.1.15's `XrefStreamParser.TryReadStreamAtOffset` / `FirstPassParser.Parse` / `DefaultFilterProvider.Instance` in the present tense. `docs/STATUS.md:139` records the drift and points at this issue; the 2026-08-26 comment asks for re-verification against 0.1.16. **Still not done** — see correction 3 |
| In-process decode ceiling | `BoundedFilterProvider`, `ExtractionMaxDecodedBytes`, `decoded-size-limit` | **missing** | Grepped all three across `backend/`: zero hits. MEM-6 is a port from a parked branch, not a repair of shipped code |
| Process host / supervisor / spool | `IProcessorHost`, a supervisor, a spool directory | **missing** | Grepped `IProcessorHost` across `backend/`: zero hits; `spool` appears only as the two scheme constants in `WorkerProtocol.cs`. CF-04 `CF04-b` builds them |
| Transport contract | `ProcessorRunParams` / `ProcessorRunInput` with `contentHandle` restricted to `spool://` or `content://` by `ValidateRunParams`; `ProcessorRunLimits(DeadlineUtc, MaxWallTimeMs, MaxOutputBytes)`; `WorkerProtocolErrorCodes.ResourceExhausted = -32020`, `DeadlineExceeded = -32021` | **exists** | The IPC contract this issue's scope asks for is already typed — it does not need inventing, only hosting. Note there is **no memory-cap limit field** on `ProcessorRunLimits`; the cap is a host/OS setting, not a protocol field, which is the right place for it |
| Worker readiness | `WorkerHeartbeatRegistry` + `/health/ready` | **exists** | Reuse; do not add a second liveness surface |

**Why exit classification is the hard part.** ADR-0048's boundary only pays off if the host can tell
"OS memory kill" from "deadline exceeded" from "process crashed" from "protocol malformed" — the
blueprint says so in §7. On Windows a Job Object memory kill and a `TerminateProcess` look similar
from the parent; on Linux a cgroup OOM kill is `SIGKILL`, indistinguishable from a hard kill without
reading the cgroup's memory events. MEM-1 must define the classification and MEM-2/MEM-3 must each
prove their platform's evidence source, or every containment failure is recorded as a timeout.

## Implementation plan

**Preflight.** Read `#1429`'s body and **all four** comments — the 2026-07-25 carry-forwards
(pre-copy admission before `CopyContentForUserAsync` allocates; CCITT hostile-dimension guard) are
acceptance requirements that are not in the body's scope list. Read ADR-0047 §2, ADR-0048, and
`WORKER_PROTOCOL_V1.md` §2 and §7. Confirm CF-04's `CF04-b` state — if the supervisor does not exist
yet, MEM-1 *is* part of it.

**Sequence.** MEM-1 → {MEM-2, MEM-3} → MEM-4 → MEM-5, with MEM-6 landing independently after MEM-1.
MEM-5 is the acceptance receipt and cannot be merged on inspection.

**Producer-owned paths:** `backend/src/Taskdeck.Infrastructure/Processing/` (the launcher and the two
platform boundaries — P/Invoke and cgroup code is Infrastructure, never Application),
`backend/src/Taskdeck.Infrastructure/Services/PdfPigArtefactTextExtractor.cs`,
`backend/tests/Taskdeck.Integration.Tests/Processing/`, the bomb fixture directory.

**Integration-owner seams:** `Application/Services/ArtefactExtractionService.cs`,
`ArtefactStorageSettings.cs`, `ArtefactExtractionWarningCodes.cs` (the new memory-kill code),
`Infrastructure/DependencyInjection.cs`, CF-04's supervisor files,
`docs/platform/CONFIGURATION_REFERENCE.md` §Artefacts, ADR-0047/ADR-0048 (the 0.1.16 re-verification),
`docs/STATUS.md`.

**Rollout / rollback.** Every new key under `Artefacts` ships **disabled**; with the worker path off,
extraction takes today's in-process route (permit gate, and the ObjStm ceiling once MEM-6 lands). The
app must boot on both run paths with the worker both off and on. **Fail closed once enabled:** a
worker that cannot spawn, or an IPC failure, produces a warning row — never a silent fall-back to an
in-process parse. That is the pack's sharpest correct point and ADR-0048's whole purpose; record the
choice explicitly in the PR because the issue lists it as an open decision.

**Definition of done.** The bomb-fixture test passes on both platforms or is xfail-documented per
platform in CI with a tracked follow-up (the issue permits this). The acceptance receipt records what
blueprint §9 lists: fixture id/hash and why it exercises the xref path, the configured cap, host and
worker peak memory, OS/platform/runtime versions, the observed exit classification, exactly one failed
run and one warning row, capture and source asset still readable, no leaked process or spool file,
and the repeated-run result. The malicious payload never appears in a log or an issue comment —
only the controlled fixture path and hash.

## Test plan

- [ ] Infrastructure: exit classification — a fake launcher returning each terminal condition maps to exactly one outcome, and memory-kill is never recorded as `extraction-timeout`
- [ ] Infrastructure (Windows): the child is assigned to the Job Object **before** any untrusted work; the cap kills it; `KILL_ON_JOB_CLOSE` reaps the tree; a grandchild does not survive
- [ ] Infrastructure (Linux/container): the cgroup memory maximum kills the group; the pids limit holds; the filesystem is read-only except the spool; the network is denied
- [ ] Integration: **the bomb proof** — a crafted xref-stream FlateDecode fixture drives worker memory to the cap, the worker is killed, exactly one warning-bearing history row is written, the artefact stays stored, the host does not OOM, and a repeat run leaves the host healthy — live acceptance box 1
- [ ] Integration: the permit gate and the current in-process extraction contract pass **unchanged** — live acceptance box 2
- [ ] Integration: `ExtractionMaxDecodedBytes` ObjStm parity/abort tests pass with the repaired `BoundedFilterProvider`, including a corrupt Flate header, an LZW stream that under-declares, and an unguarded RunLength stream (`#1417` HIGH-1/HIGH-2/MEDIUM-3) — live acceptance box 3
- [ ] Integration: **pre-copy admission** — a saturated gate returns 429 *before* `CopyContentForUserAsync` allocates the payload, with a deterministic saturation-ordering test (2026-07-25 carry-forward)
- [ ] Integration: a hostile CCITT rows/height fixture does not allocate before the guard, and the worker cap contains it (2026-07-25 carry-forward)
- [ ] Integration: no spool file survives a terminal state, a cancellation or a crash; the startup scavenger clears stale ones
- [ ] Integration: an unspawnable worker fails closed to a warning row and never parses in process while the contained path is enabled
- [ ] Api: the app boots on both run paths with the worker disabled and with it enabled — live acceptance box 4
- [ ] Api: stderr from the worker is bounded and content-free; no source text, no file name beyond the spool handle
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1` — platform P/Invoke must not leak into Application
- [ ] Docs: `node scripts/check-docs-governance.mjs`, plus the ADR-0047/ADR-0048 0.1.16 re-verification note

## Edge cases

- The worker dies before the handshake — one rejected run, no orphan, no leaked spool file.
- **Memory kill racing a deadline** — both fire; one terminal outcome and one reason, never a timeout
  *and* a kill row. The blueprint's §6 step 7 says this; it is the easiest invariant to lose.
- A child that spawns grandchildren; on Windows an antivirus-induced launch delay that looks like a
  describe timeout.
- Host cancellation while the spool file is still being written; a stream larger than its declared
  size; a partial JSON frame at EOF.
- A stale spool file from a previous crash — the scavenger's threshold must be documented, not implicit.
- The cap set below what a legitimate large PDF needs — every real document then fails with the
  memory-kill warning. Pick and document defaults per platform (an open decision in the issue).
- CI cannot enforce a Job Object or a cgroup in every runner — the issue permits per-platform xfail
  with a tracked follow-up; take it explicitly rather than weakening the assertion.
- PdfPig 0.1.16 may have changed the xref path — if it has, ADR-0047/ADR-0048 need a correction note
  and MEM-5's fixture may need a different trigger. The direction of the current claim is
  conservative (it asserts a gap), so it cannot act as a false safety property in the meantime.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Blueprint | `docs/analysis/2026-08-30-acceleration-bundle/architecture/WORKER_CONTAINMENT_BLUEPRINT.md` | §6 the seven-step cancellation/termination ladder, §7 the per-OS containment lists (Windows Job Object; cgroup v2 + pids + read-only FS + dropped capabilities + non-root), **§9 the decompression-bomb acceptance receipt** — the single most directly usable page in the bundle for this issue | Read its 2026-09-02 validation preface. §8's conformance list has 12 items where the spec has 11 |
| Diagram | `.../diagrams/worker-containment.svg` (`.dot` beside it) | runner → registry → host → supervisor → **OS boundary** → processor → validator → commit, with "no domain mutation tools" on the processor node | Explanatory; the commit step is CF-03's, not this issue's |
| C# candidate | `.../candidates/dotnet/WorkerSessionProof.cs` (+ tests) | The launch-time secret env var and the challenge/proof shape the supervisor needs before it can trust the child | Reference only, and it belongs to CF-04 `#2258`, not here. See the CF-04 file's Candidate defects — no replay rejection, unspecified protocol-version canonical form, secrets leaked into managed strings |
| Test vectors | `.../testing/test-vectors/worker-protocol-invalid.sample.json` | The `wrong-session-proof` and `oversized-frame` case names as part of the supervisor's adversarial list | Its `expect_error` tokens are snake_case strings that exist nowhere in Taskdeck |
| Docs draft | `.../docs-drafts/PROCESSOR_CONFORMANCE_CHECKLIST.md` | Two items directly relevant here: spool/process resources cleaned after **every** terminal state, and "default install works with the processor disabled" | Adapt into CF-04's spec §6; do not publish as a parallel checklist |

## Corrections to the bundle

1. **The pack's dependency metadata is cyclic and both halves are wrong in the same way.** `1429.md`
   says *Depends on: #2257, #2258 / Unblocks: #2258*, while `2258.md` says *Unblocks: #1429*. The real
   shape: MEM-1..MEM-3 are the **internals** of CF-04's supervisor (`CF04-b`/`CF04-c`) and land there;
   MEM-4/MEM-5 depend on that supervisor existing; CF-04's acceptance box 1 is then closed by MEM-5.
   One supervisor, one order, no cycle.
2. **Pack lists `#2257` (CF-03) as a dependency.** Over-stated. A contained parse writing one
   `ArtefactExtraction` history row needs no `ProcessingJob` and no `ProcessingRun`. Only CF-04
   conformance items 9 and 10 do. Gating the containment boundary on the job substrate delays a
   Priority I security boundary behind an unrelated schema.
3. **Pack says nothing about the PdfPig version, and the issue's own 2026-08-26 comment is still
   open.** `backend/Directory.Packages.props:51` pins **0.1.16**; ADR-0047 (lines 20, 59) and ADR-0048
   (lines 15, 53) describe **0.1.15**'s `XrefStreamParser.TryReadStreamAtOffset` hard-coded
   `DefaultFilterProvider.Instance` in the present tense, and `docs/STATUS.md:139` records the sentinel
   move without resolving it. Re-verifying against 0.1.16 is a preflight step for MEM-5, because the
   fixture has to exercise a path that still exists.
4. **Pack's MEM-6 says "Port and repair `BoundedFilterProvider`".** Accurate — and worth stating that
   there is nothing on `main` to repair: grepped `BoundedFilterProvider`, `ExtractionMaxDecodedBytes`
   and `decoded-size-limit` across `backend/`, all zero. It is a port from the parked `#1417` branch,
   and the four findings plus the two 2026-07-25 carry-forwards are gates on the port, not follow-ups.
5. **Pack's "avoid: unbounded MemoryStream IPC".** Correct, and already partly solved: the shipped
   protocol restricts a `contentHandle` to `spool://` or `content://` and `ValidateRunParams` rejects
   anything else, so the "arbitrary input paths" risk is closed at the contract level. What is *not*
   solved is the spool implementation itself, which does not exist.
6. **Pack's "avoid: fallback to in-process after a containment failure".** The single most important
   line in the pack. It is currently an **open decision** in the issue ("fail closed versus
   in-process fallback"), and the pack's recommendation (fail closed once enabled) is right; make it a
   recorded decision in MEM-1 rather than an implementation habit.
7. **Neither the pack nor the issue says which warning code a memory kill gets.** The shipped
   `ArtefactExtractionWarningCodes` has nine values and no memory-kill member, and
   `ArtefactExtractionService.cs:132` already documents that conflating `extraction-timeout` with
   `extractor-error` loses information. Add one stable code; do not reuse `extraction-timeout`.
8. **The pack's "IPC contract" scope item is largely already typed.** `ProcessorRunParams`,
   `ProcessorRunInput`, `ProcessorRunLimits`, `ProcessorRunResult` and `WorkerProtocolErrorCodes`
   (`-32020` resource exhausted, `-32021` deadline exceeded) exist on `main`. Note also what is
   deliberately absent: there is **no memory-cap field** on `ProcessorRunLimits` — the cap is an OS/host
   setting, and adding it to the wire would let a processor negotiate its own containment.
9. **Pack's suggested-image block** uses `../path/to/worker-containment.svg`; the bundle's
   issue-comment file uses `docs/architecture/diagrams/worker-containment.svg`, which does not exist.
   The diagram is archived at
   `docs/analysis/2026-08-30-acceleration-bundle/diagrams/worker-containment.svg`.
10. **Vocabulary check:** clean. The pack keeps the human-owned boundary implicit, though — the issue's
    "Long-term exit" is explicit that upstream PdfPig disclosure/filing is **HUMAN-OWNED** and no agent
    may file it. Carry that into any completion receipt.
