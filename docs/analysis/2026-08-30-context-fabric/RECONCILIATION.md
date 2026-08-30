# Context Fabric pack — reconciliation against the repository (2026-08-30)

Last Updated: 2026-08-30

**What this is.** The maintainer produced a planning pack with an external LLM on 2026-08-30 (the files
beside this record: blueprint, draft ADR as received, worker-protocol proof of concept, manifest schema
and example, issue seeds, diagram) against repository baseline `2807c0b5c` (PR `#2244`). This record is
the agent pass that verified the pack's claims against `main` `09633db82`, re-thought the parts that
needed it, and turned the rest into ADR-0065, the CF-NN issue wave, the release ladder to v1.0, and the
first scaffolding PR. Every claim below that names a file was read in this pass.

## 1. Verified pack claims (accurate)

| Pack claim | Evidence on `09633db82` | Verdict |
| --- | --- | --- |
| A capture is an `LlmRequest` row with a serialised `CapturePayloadV1`; status, provenance, disposition and retry state live in the row/JSON | `Application/Services/CaptureService.CreateAsync` → `new LlmRequest(userId, ResolveRequestTypeForSource(source), SerializePayload(payload), boardId)`; `DTOs/CaptureContracts.cs` (`CaptureProvenanceV1`, `CaptureDispositionV1`); `Enums/CaptureStatus.cs` (`CaptureStatusPolicy.MapFromQueueStatus`) | **True.** ADR-0005 named this exit condition itself |
| `CaptureSource` mixes modality, transport, origin and producer | `Enums/CaptureSource.cs` — twelve values from `Typed` to `VsCodeExtension` | **True** |
| Routing is by request-type string and dedicated SQL predicates; a dedicated transcript worker | `CaptureRequestContract.RequestTypeTranscriptV1`; `Infrastructure/Repositories/LlmQueueRepository.cs` (`inbox.capture.%`, `inbox.capture.transcript.%`); `Api/Workers/TranscriptTriageWorker.cs`, `LlmQueueToProposalWorker.cs` | **True** (ADR-0045 chose it deliberately) |
| Immutable `SourceArtefact` + separate `ArtefactBlob` + append-only `ArtefactExtraction`; first-MIME-match extractor registration | `Domain/Entities/SourceArtefact.cs`, `ArtefactBlob.cs`, `ArtefactExtraction.cs`; `ArtefactExtractionService` over `IEnumerable<IArtefactTextExtractor>` (`PlainText`, `PdfPig`) | **True** |
| Transcript segments are line-indexed with one timestamp; evidence is transcript-typed while other sources are generic strings | `TranscriptSegment { StartLine, EndLine, Speaker?, TimestampMilliseconds? }`; `ProvenanceEvidenceLink` (`SourceType`/`SourceId` strings, typed `TranscriptId` only for Transcript, char spans) | **True** |
| No blob abstraction exists | grep for `IBlobStore` / `IArtefactBlobStore` across `backend/src`: none | **True** |
| ADR-0046 chose cloud-vision-first images and rejected local OCR at MVP | ADR-0046 decision 5 and its "Local-OCR-first image intake" rejection | **True** |
| Triage requires a board; chat cannot propose without a board | `CaptureTriageService` (GEN-04 trap on `#1318`); `ChatService.cs` gates on `session.BoardId.HasValue` (`#2004`) | **True** |
| ADR-0057 is direction only; ADR-0060 keeps boards as the shipped model with stages 4–5 gated | ADR index rows; ADR-0060 "compat-path" ruling | **True** |

## 2. Corrections to the pack

| Pack statement | Correction |
| --- | --- |
| Draft ADR numbered **0063** | Already used (archived-board card write protection); **0064** was claimed by PR `#2252` during this pass; the record is **ADR-0065** |
| "REVIVAL M4 WhisperX" | No such item exists; the audio/WhisperX line is REVIVAL-08 phase 2b in `docs/REVIVAL_PLAN.md`, gated on transcript paste proving value — which the 2026-08-27 acceptance walkthrough delivered |
| Triage v2 `type` exists only as `action` | The prompt already asks for `action | decision | question` (`LlmCaptureTriagePrompt.cs`); candidates are a formalisation of a boundary the model already sees, not a new concept |
| Utility-scored routing from the start | Adopted only after a benchmark corpus exists (ADR-0065 §Decision 8); router v1 is constraints + ordered preference + a persisted route receipt |
| Price anchors (Deepgram, AssemblyAI, Google, OpenAI) | Recorded as pack-reported, dated, **unverified by this repository**; CF-15 re-measures before any default |
| Presentation profiles as a new selector | Must replace, not sit beside, the shipped `guided / workbench / agent` modes (`#1972` proves `agent` is byte-identical to `workbench`) |
| Authority "Ask / streamlined / review" wording | Mapped onto ADR-0057's already-ratified presets (Observe · Suggest · Assist · Operate · Autonomous · Custom); no parallel vocabulary |
| Storage: object store for hosted | Only at ADR-0061 stage 3 (managed SaaS); stages 1–2 stay SQLite; single-file ownership unchanged |
| Voice: WhisperX as the default path | Two paths: lightweight local STT for short notes (CF-13), WhisperX/cloud diarising STT for meetings (CF-14/CF-15); ADR-0033's `webkitSpeechRecognition` rejection stands |

## 3. The re-think (what the agent pass changed or added)

1. **The ADR-0048 worker is the first sidecar.** The memory-capped extraction worker (`#1429`) was
   already decided; making it the first host of the Taskdeck Worker Protocol proves the supervisor on
   PdfPig before any Python/CUDA process. One supervisor, two consumers (CF-04).
2. **Foundation before hosted stranger data.** The ID-preserving `Capture` migration is cheapest while
   every user is the maintainer; so the behaviour-preserving slices (CF-01..07, CF-23) join v0.4 beside
   the hosted beta, and the visible payoff (voice, Universal Capture, candidates) is v0.5. The
   maintainer's same-day "hosted = v0.4" ruling is preserved.
3. **Candidates are a separate axis from item types.** ADR-0060's ruling that triage `type` is "a
   different axis" is honoured: Decision/Question/Risk/Fact/Reference candidates are records and
   registers, never new card types.
4. **Measurement precedes adaptivity.** No learned weight, no "auto" that changes what the user sees,
   before CF-24's corpus and metrics exist; the same corpus gates the first delegated-authority slice.
5. **The context resolver is shared with chat.** `#2004`'s root cause 1 and `#2141`'s dead end are the
   same defect; CF-09 fixes both.
6. **No new `CaptureSource` values, no new lane predicates.** GEN-04 (`#1318`) is closed as superseded;
   its halves live in CF-05/CF-07/CF-08/CF-09.

## 4. Release ladder (ruled under delegation; themes beyond v0.5 are LEANING)

| Release | Theme | Content | Milestone |
| --- | --- | --- | --- |
| v0.3 | Accountable Agents + Downloadable Beta | in flight, untouched | 4 |
| v0.4 | Hosted Open Beta + Work Model + **Fabric Foundation** | `#2243` hosted beta, work-model slices, `#1429` as first sidecar, CF-01..07, CF-23 | 5 |
| v0.5 | **Speak, Type, Paste, or Drop** | CF-08, CF-09, CF-12, CF-13, CF-14, CF-16, CF-20, CF-21, GEN-03 `#1317`, GEN-06 `#1320` | 6 |
| v0.6 | **Under Your Rules** | CF-10, CF-11, CF-15, CF-17, CF-18, CF-22 (own gate), CF-24 | 7 |
| v0.7 | Project Companion | ADR-0060 stage 4 Project (needs an ADR-0060 amendment), dossiers GEN-07, Today GEN-08, registers, integrations as sources, MCP as origin, notifications `#2010` | 8 |
| v0.8 | Teams and Trust | ADR-0061 stage 2, participants/invites, approval chains, audit/egress reports, signed installer, SBOM, macOS | 9 |
| v0.9 | Scale and Steadiness | slice 8 on measured demand, performance pass, contract-freeze candidates, it/es review, framework major | 10 |
| v1.0 | General Availability | promise truthfully supported end to end; frozen export/API v1; security review; commercial model and name residuals settled; zero Priority I | 11 |

Alternatives weighed for the v0.4/v0.5 order: (B) swap — Fabric + voice first, hosted second — has one
strong argument (the maintainer's own dogfooding says the product does not stick yet, and beta attention
is a one-shot resource) and was not chosen because the hosted work is ops/security, parallel-safe, and
already human-gated; (C) hosted first with the whole wave after — rejected because it puts the
ID-preserving migration behind stranger data. The maintainer can overturn on CF-00.

## 5. Issue map and re-pointing

Tracker CF-00 `#2254` carries the delegated rulings and the map; children CF-01 `#2255` … CF-24 `#2277`
(CF-19 = GEN-03 `#1317`). Re-pointed with dated comments: `#1327` GEN-00, `#1317` (→ v0.5), `#1318`
(closed, superseded), `#1320` (→ v0.5, child of CF-20), `#1323`, `#1429`, `#1276`, `#2141`, `#2004`,
`#2089`, `#1972`. Milestones 5 (renamed), 6, 7, 8, 9, 10, 11 created 2026-08-30.

## 6. What the scaffold PR lands (behaviour-preserving)

Domain vocabulary and mapping, the `Capture` aggregate with an empty `Captures` table, `ICaptureStore`
(`EfCaptureStore`), `IRepresentationStore` / `IBlobStore` contracts (unregistered), the processor
manifest model + validator + schema, the worker-protocol envelopes + validator, and the
`ContextFabric:DualWriteCaptures` flag (default `false`). See `docs/architecture/CONTEXT_FABRIC.md` §2
for the file map and §7 for the proving checks.

## 7. Not verified in this pass

- The pack's cost numbers and provider claims (not re-measured; CF-15).
- The pack's interactive dashboard (`context-fabric-dashboard.html`, kept out of the repository) beyond
  extracting its migration map and first-slice text.
- Any runtime behaviour of the scaffold under `DualWriteCaptures=true` in a live host (unit-tested
  through `CaptureServiceDualWriteTests` only; the flag stays off by default).
