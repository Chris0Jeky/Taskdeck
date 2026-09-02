# CF-18 — Local OCR sidecar processor (image.ocr): OCR-first image path, amends ADR-0046 decision 5 (#2272)

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Planning input, not authority: the live issue, ADR-0065/ADR-0057 and `docs/architecture/CONTEXT_FABRIC.md` win. Corrections to the bundle are listed in the last section.

## Outcome

An optional, protocol-conformant local OCR sidecar (`image.ocr`) that turns an image `SourceAsset` into
an `OcrText` representation with normalised `ImageRegion` anchors, runs inside hard resource bounds
under **the one CF-04 supervisor**, and escalates to remote `image.describe` only when a deterministic
sufficiency rule says the OCR is insufficient **and** profile plus consent allow it. Otherwise the
capture shows an honest "not extracted" state.

## Live dependencies (verified 2026-09-02)

| Issue | State | What it must deliver first | Unblocks |
| --- | --- | --- | --- |
| CF-04 `#2258` worker host | open (umbrella, unsplit) | the **only** sidecar supervisor: `IProcessorHost`, spool dir, session secret, deadlines, output caps, network denial, conformance suite. None of this exists on `main` | 02, 03, 04 |
| `#1429` ADR-0048 worker | open | the memory-capped process host (Windows Job Object / cgroup) that CF-04 first proves on PdfPig — CF-18 inherits it, never rebuilds it | 04 |
| CF-06 `#2260` representations | open | durable `Representation` + quality state for the OCR payload | 03, 07 |
| CF-07 `#2261` anchors | open | `EvidenceAnchor.ImageRegion` and the viewer contract | 03, 07 |
| CF-10 `#2264` profile | open | egress class, **destination consent**, budgets, escalation policy | 05, 06 |
| GEN-03 `#1317` | open | the registered `image.describe` vision processor CF-18 escalates *to*; ships first per ADR-0065 §Image ruling | 06 |
| GEN-09 `#1323` | open | untrusted-content prompt rails — OCR text is attacker-controlled input | 05, and admission generally |
| CF-24A `#2319` | open | screenshot corpus + benchmark command for the engine spike | 01 |

## Child slices (one PR each, in order)

| id | Outcome | Depends on | Mode | Startable now? |
| --- | --- | --- | --- | --- |
| `V06-CF18-01-engine-spike` | Benchmark PaddleOCR-class engines on the CF-24A corpus, select one | — | contract-only | **Partly.** A written spike protocol and comparison criteria can be drafted now; the measurement itself needs the CF-24A corpus `#2319` |
| `V06-CF18-02-manifest` | `image.ocr` manifest with input/output/resource contracts | 01 | implementation | **Yes, narrowly.** `ProcessorManifest` + validator + schema are already on `main`, so a `local`/`sidecar` manifest and its validator tests can land ahead of CF-04 — as a contract, not a wiring |
| `V06-CF18-03-adapter` | Map OCR lines/words/regions into a `representation` output | 02 | implementation | No — CF-04/CF-06/CF-07 |
| `V06-CF18-04-containment` | Prove pixel/page/output/memory/deadline bounds and content-free stderr | 03 | implementation | No — `#1429` supervisor |
| `V06-CF18-05-sufficiency` | Explainable OCR sufficiency classification | 03 | implementation | No — needs real OCR facts |
| `V06-CF18-06-escalation` | Route `image.describe` only under profile + consent | 05 | implementation | No — GEN-03 + CF-10 |
| `V06-CF18-07-viewer` | Exercise `ImageRegion` evidence through the viewer contract | 03 | implementation | No — CF-07; and the image viewer itself is new |

## Architecture

**Path.** `Image SourceAsset → content.inspect → image.ocr (local sidecar) → OcrText Representation +
ImageRegion anchors → sufficiency decision → { sufficient: semantic.extract | insufficient + consented:
image.describe | insufficient + not consented: honest incomplete state }`.

**Containment is inherited, never rebuilt.** ADR-0048 fixes the boundary as a **process-level memory
cap on one dedicated worker process** — Windows Job Object `JOB_OBJECT_LIMIT_JOB_MEMORY` for the native
path, a container/cgroup limit under Docker — hosting exactly one parse at a time, with a kill reported
as a content-free warning outcome. `#1429` delivers that host, config keys default-OFF, with a
bomb-fixture test on both paths; CF-04 makes it the Worker Protocol host. **CF-18 must not create a
second process host** (ADR-0065 §Decision 10; the previous reconciliation's §Worker containment says the
same). Shipped today: only the permit gate — `Application/Services/ArtefactExtractionGate.cs`
(`ExtractionMaxConcurrency`, default 2, ADR-0047 §1) — and the wall-clock budget in
`ArtefactExtractionService.cs`. No Job Object, cgroup, `IProcessorHost` or registry code exists on `main`.

**Bounds the manifest and host must declare:** input byte size, decoded dimensions, total pixels,
page/frame count, process memory, wall time, output characters, region count, content-free stderr,
network denied. `ProcessorManifestValidator` already enforces that a `local` processor cannot declare
`networkRequired: true` and cannot `execute: remote`, and that a `networkRequired: false` manifest lists
no `allowedHosts`.

**Representation shape is already specified** by `docs/architecture/WORKER_PROTOCOL_V1.md` §3.5: a
`representation` output of kind `OcrText` carrying `text` (may be empty, never absent), `segments`
(char + time) and `regions` — normalised rectangles with `0 ≤ x, y`, `width, height > 0`,
`x + width ≤ 1`, `y + height ≤ 1`, `pageNumber ≥ 1` when present, and an optional char range within
`text`. CF-18 defines no coordinate convention of its own; it documents and snapshot-tests the one the
protocol fixes. `EvidenceAnchorKind.ImageRegion` already exists in
`backend/src/Taskdeck.Domain/Enums/EvidenceAnchorKind.cs`.

**New:** `OcrRepresentationPayload`, `OcrSufficiencyDecision`, `ImageProcessingLimits`,
`OcrEscalationReceipt`, `OcrSidecarProcessor`, `IImagePreflightInspector`, `IOcrSufficiencyEvaluator`,
`IOcrVisionFusion`, and the sidecar itself (a new `processors/taskdeck-ocr/` tree).

**Threat model.** OCR output is untrusted content: it enters `semantic.extract` as delimited data,
never as instructions (ADR-0065 rule 6, GEN-09 `#1323`), typed candidate validation stays on, and human
review stays on. See `docs/security/` for the existing threat-model records. Format validation reuses
`Application/Services/ArtefactContentValidator.cs` (magic-byte checks) rather than a new sniffer.

## Implementation plan

**Preflight.** Read `#2272`, ADR-0048, ADR-0046 decision 5 and ADR-0065 §Image ruling; confirm `#1429`
has landed the memory-capped host **and** that CF-04 hosts the protocol on it; confirm GEN-03 `#1317`
shipped as one registered processor; confirm GEN-09 `#1323` rails are effective. Run
`dotnet test backend/tests/Taskdeck.Application.Tests/... --filter "FullyQualifiedName~Processing"`
before editing.

| Path | State | Owner |
| --- | --- | --- |
| `processors/taskdeck-ocr/` | to be created (no `processors/` tree exists) | producer |
| `backend/src/Taskdeck.Application/Processing/Ocr/` | to be created | producer |
| `backend/tests/Taskdeck.{Domain,Application,Api}.Tests/Processing/Ocr/` | to be created | producer |
| `scripts/context_fabric/benchmark-ocr.*` | to be created beside `scripts/context_fabric/check_contract_drafts.py` (directory exists since `#2371`) | producer |
| `backend/src/Taskdeck.Application/Processing/Protocol/WorkerProtocol.cs` | exists — CF-04 owns it; CF-18 should need no change | integration owner |
| `backend/src/Taskdeck.Application/Processing/Schemas/processor-manifest.v1.schema.json` | exists | integration owner |
| `backend/src/Taskdeck.Infrastructure/Migrations/TaskdeckDbContextModelSnapshot.cs` | exists | integration owner |
| `frontend/taskdeck-web/src/views/paper/PaperReviewView.vue` | exists (**not** `views/paper/ReviewView.vue`) | integration owner |
| image evidence viewer component | to be created — only `components/review/TranscriptEvidenceViewer.vue` exists | producer |

**Rollout / rollback.** The sidecar ships unregistered and default-OFF; enabling it registers a manifest.
Rollback unregisters the processor — existing OCR representations, anchors and receipts stay readable and
name a now-absent processor. Escalation stays off until CF-10 consent exists.

**Definition of done.** No bespoke process host. All three live acceptance boxes traced to tests. Export,
import and account deletion cover OCR representations, regions and escalation receipts
(`DataExportService`, `ExportImportService`, `AccountDeletionService`). Engine choice and thresholds are
configuration with a dated benchmark, never hard-coded claims.

## Test plan

- [ ] Text-dominant screenshot → regions produced, **zero** remote calls under *Balanced* (Application + Api).
- [ ] Diagram fixture insufficient, no consent → escalation denied, honest "not extracted" state (Application).
- [ ] **Containment:** decoded-pixel limit enforced before unbounded allocation where possible, and by the process cap otherwise; a bomb fixture kills the worker with one failed run and no host OOM (Api/Integration, both Job Object and cgroup paths — cgroup needs Docker).
- [ ] **Containment:** network denied for a `local` manifest; deadline and `maxOutputBytes` yield `-32021`/`-32022`.
- [ ] **Content-free logging:** stderr carries no extracted text, no file names beyond the spool handle; a content-bearing stderr line fails conformance.
- [ ] Region geometry and char spans validate against the output text; out-of-range or NaN rejected by the validator, never thrown (Domain).
- [ ] Malformed container → content-free diagnostic; the source asset stays readable (Application).
- [ ] Hostile OCR text enters `semantic.extract` as data and cannot escape the output schema (Application, GEN-09).
- [ ] EXIF-rotated and very tall images preserve coordinate origin (snapshot test).
- [ ] Owner isolation on representations, regions and receipts (Api).

Commands: `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Ocr"`;
`dotnet test backend/tests/Taskdeck.Domain.Tests/... --filter "FullyQualifiedName~Ocr"`;
`dotnet test backend/tests/Taskdeck.Api.Tests/... --filter "FullyQualifiedName~Processing"`;
`cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <region-viewer spec>`.

## Edge cases

EXIF rotation flips coordinate orientation · animated / multi-frame input · very tall screenshot ·
transparent or low-contrast text · mixed-script pages · engine returns overlapping boxes or NaN
confidence · OCR text sufficient but diagram semantics absent · remote vision route unhealthy after the
sufficiency decision · decompression bomb · hostile dimensions · excessive output lines · a historical
representation references an uninstalled processor · account deletion races a running sidecar.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/candidates/csharp/OcrSufficiencyPolicy.cs` | Deterministic sufficiency facts + six stable reason codes; a good starting rule set | Namespace `Taskdeck.Acceleration.V06`; thresholds are placeholders needing CF-24A measurement; `LayoutDependent`/`DiagramLike` are asserted inputs with no producer |
| C# candidate | `.../candidates/csharp/ConsentGrant.cs`, `ProcessingProfile.cs`, `SelectiveEscalationPlanner.cs` | The consent/escalation shapes the escalation slice consumes | Owned by CF-10/CF-11, not CF-18 |
| Schema | `.../schemas/benchmark-observations.schema.json` | `ocrAccuracy` row shape for the engine spike | No per-language breakdown; unknown is `null` |
| Fixture | `.../fixtures/benchmark-observations.example.json` | Example OCR observation row | Illustrative numbers only |
| Diagram | `.../diagrams/local-ocr-path.svg` | The OCR-first path and its escalation branch | Advisory |
| Bundle doc | bundle `03_ARCHITECTURE/LOCAL_OCR.md` | Bounds checklist, sufficiency facts, escalation preconditions, threat model | Read with the corrections below |

## Corrections to the bundle

1. **Containment is not "prove bounds"; it is "inherit `#1429`".** The pack's `containment` slice reads as if CF-18 implements the caps. ADR-0048 fixes the boundary as a process memory cap delivered by `#1429`, and ADR-0065 §Decision 10 makes that same worker the first host of the Worker Protocol. The pack's own DoD line says CF-18 "may not introduce a bespoke process host" — but its slice list does not make `#1429` a *code* dependency, only an admission one. It is both, and none of that host exists on `main`.
2. **`frontend/taskdeck-web/src/views/paper/ReviewView.vue` does not exist.** The review shell is `PaperReviewView.vue`.
3. **There is no existing image evidence viewer.** The pack's slice 07 says "exercise `ImageRegion` evidence through the *existing* viewer contract". The only shipped evidence viewer is `components/review/TranscriptEvidenceViewer.vue` (transcript/time). `ImageRegion` exists solely as an `EvidenceAnchorKind` enum value and the protocol's region validation rules. The image viewer is new work, and CF-07 `#2261` owns the contract.
4. **Consent does not exist.** As with CF-15, `backend/src` has no processing or destination consent model (the word appears only in prose about the batch-execution approved-revision pin, an unrelated concept). "Destination consent active" is CF-10 `#2264` new work.
5. **GEN-03 `#1317` and GEN-09 `#1323` are correctly listed as blockers** — this is a place the pack is *stricter* than the live issue, which names only `#1317`. Keep the pack's version: ADR-0065 rule 6 makes prompt rails a hard condition for feeding OCR text into extraction.
6. **`WorkerProtocol.cs` should not need editing.** Listing it as a coordinator seam is fine as a fence, but an OCR change to the protocol would mean the representation contract was mis-specified; escalate to CF-04 rather than patch it.
7. **Missing paths.** `processors/`, `backend/src/Taskdeck.Application/Processing/Ocr/` and `scripts/context_fabric/` are all new directories.
8. **Protocol is v1-alpha.** It is fixed only once PdfPig (`#1429`) *and* WhisperX (CF-14 `#2268`) pass CF-04 conformance — so a CF-18 manifest written today may need field additions.
