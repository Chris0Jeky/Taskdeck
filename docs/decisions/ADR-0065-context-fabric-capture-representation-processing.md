# ADR-0065: Context Fabric — Durable Capture, Derived Representations, Semantic Candidates, and Capability-Based Processing

- **Status**: Proposed (2026-08-30). **Not in force.** Nothing below is buildable until the
  maintainer records the acceptance conditions in §Acceptance conditions (tracker CF-00). Until
  then the shipped capture model (ADR-0005), the transcript lane (ADR-0045), the generalist wave
  (ADR-0046), and review-first automation (ADR-0003 / GP-06 / ADR-0056) remain fully operative.
- **Date**: 2026-08-30
- **Deciders**: Chris0Jeky (maintainer)
- **Source**: the maintainer's 2026-08-30 *Context Fabric* planning pack (an external LLM planning
  session against repository baseline `2807c0b5c`, PR `#2244`), reconciled against `main`
  `09633db82` by the agent pass recorded in
  `docs/analysis/2026-08-30-context-fabric/RECONCILIATION.md`. The pack itself (blueprint, worker
  protocol proof of concept, processor manifest schema, issue seeds, diagram) is archived beside that
  record; the pack's draft ADR was numbered 0063, and 0064 was claimed by PR `#2252` while this record was being written; both numbers were already used, so this is ADR-0065.
- **Related**: ADR-0005 (capture queue-wrapper — *superseded* once §Decision 2 ships), ADR-0033
  (ambient channel: VS Code over desktop voice — *partially superseded* by §Voice ruling), ADR-0045
  (transcript lane — *adapted*, not replaced), ADR-0046 (generalist expansion — *amended* in
  decisions 4 and 5), ADR-0047/ADR-0048 (extraction permit gate and memory-capped worker process —
  *reused* as the first sidecar host), ADR-0057 (delegated authority — *implemented in shape only*,
  still direction-only), ADR-0060 (canonical work model — *respected*: candidate kinds are a
  separate axis from item types, and the compiler targets `Card` until stage 5), ADR-0061 (hosted
  boundary — object storage only at its stage 3), GP-06, GP-08, GP-09, GP-10.

## Context

Taskdeck's strategy names **context-to-action** as its engine and the transcripts / messy notes /
quick captures / agent requests wedge as its next releases (`docs/strategy/PRODUCT_DIRECTION.md` §1).
The maintainer's 2026-08-30 pack sharpens that into an "anything enters" thesis — speak, type,
paste, or drop — and asks how the current architecture should carry voice, images, documents,
integrations, and agents without becoming a pile of modality-specific pipelines.

The repository already holds most of the hard trust substrate (verified 2026-08-30 against
`09633db82`; file references are the entry points, not a rewrite list):

- immutable `SourceArtefact` metadata with a separate one-to-one `ArtefactBlob` in SQLite
  (`backend/src/Taskdeck.Domain/Entities/SourceArtefact.cs`, `ArtefactBlob.cs`; `ArtefactKind` =
  `Image | Pdf | TextFile`), append-only `ArtefactExtraction` records naming extractor and version,
  and an `IArtefactTextExtractor` list (`PlainText`, `PdfPig`) selected first-match by MIME;
- a durable, LF-normalised `Transcript` (≤200k chars) with line-indexed `TranscriptSegment`s that
  carry a speaker and a single timestamp (`Transcript.cs`, `TranscriptSegment.cs`);
- `ProvenanceEvidenceLink` with generic `SourceType`/`SourceId` strings, a typed `TranscriptId` FK
  only for the Transcript source type, and half-open UTF-16 char spans (ADR-0045 §7);
- proposal operations, revisions, Preview == Apply, risk classification, outcomes, receipts, and
  the ADR-0043 quality-feedback signal;
- `LlmUsageRecord` (provider, model, input/output tokens per surface) and per-user quotas;
- ADR-0057's accepted separation of duties (proposer ≠ policy engine ≠ executor) and ADR-0060's
  accepted work-model vocabulary.

Three structural debts stand in the way of the thesis:

1. **A capture is a queue row.** `CaptureService.CreateAsync` persists every capture as an
   `LlmRequest` whose `Payload` is a serialised `CapturePayloadV1`; provenance
   (`CaptureProvenanceV1`), disposition (`CaptureDispositionV1`), triage-run and proposal linkage,
   and the provider/model that produced the result all live *inside that JSON*, and Inbox status is
   `RequestStatus` mapped through `CaptureStatusPolicy`. ADR-0005 chose this deliberately as an MVP
   and named its own exit condition — "promote to a dedicated table when capture-specific queries,
   retention policies, or volume require it". Voice retention, multi-asset captures, offline intake,
   selective reruns, and more than one processor per capture meet that condition. A user-owned inbox
   item and a disposable processing job have different lifecycles; fusing them makes every new
   modality, retry mode, and processor harder.
2. **`CaptureSource` mixes dimensions.** Its twelve values (`Typed`, `Paste`, `TranscriptPaste`,
   `Import`, `Voice`, `MeetingIntegration`, `TranscriptFile`, `MarkdownImport`, `WebClip`,
   `ShareTarget`, `BrowserExtension`, `VsCodeExtension`) interleave modality, transport, origin
   adapter, and producer. `SourceArtefact` and `Transcript` both carry the same enum.
3. **Routing is by request-type string.** `CaptureRequestContract.ResolveRequestTypeForSource`
   chooses `inbox.capture.v1` or `inbox.capture.transcript.v1`; `LlmQueueRepository` bakes the lane
   split into raw-SQL `LIKE` predicates; `TranscriptTriageWorker` and `LlmQueueToProposalWorker`
   (`backend/src/Taskdeck.Api/Workers/`) each own a lane. ADR-0045 chose this so a slow LLM call
   would not block the millisecond capture lane — a sound local optimisation. GEN-04 (`#1318`) plans
   to route artefact text by *pretending it is a transcript*; multiplying that pattern for audio,
   OCR, vision, documents, email, and connectors would multiply workers, predicates, and retry logic
   per input type.

Two smaller facts shape the decision. Triage schema v2 already asks the model for
`type ∈ {action, decision, question}` (`LlmCaptureTriagePrompt.cs`), so semantic kinds beyond
"task" already exist at the model boundary but are flattened into card operations. And neither
capture triage (`CaptureTriageService` requires a `BoardId`) nor chat (`ChatService` gates proposal
creation on `session.BoardId.HasValue` — `#2004`) can understand a capture without a board, which
is exactly the failure a new user hits first (`#2141`, `#2004`).

## Decision

Adopt **Context Fabric** as the internal architecture name and the following invariant for every
input, present and future:

```text
Capture → SourceAsset → Representation → SemanticCandidate → ContextBinding
        → ChangeSet → AuthorityDecision → Execution → Receipt
```

Every derived or mutating object is attributable to its input, processor, policy, and outcome.
Public wording (for `PRODUCT_DIRECTION.md`, once truthfully supported): *Speak, type, paste, or
drop. Taskdeck turns context into accountable work, under your rules.* "Anything enters" is an
architecture doctrine, not a launch claim; public copy names the optimised paths.

### 1. Capture becomes a durable aggregate

A `Capture` is the user-owned Inbox object: owner, server and optional client timestamps, origin
adapter, producer kind, intent mode, optional explicit context hint, retention policy, lifecycle
state, optional user title/note. A capture may hold several immutable `SourceAsset`s (a sentence
plus a screenshot; a voice note plus a link). A capture succeeds as soon as its sources are durably
stored; transcription, OCR, semantic extraction, and context resolution may all fail without
invalidating it.

Migration is **ID-preserving**: `Capture.Id` = the `LlmRequest.Id` of the row it is backfilled from,
so `SourceArtefact.CreatedFromCaptureId`, `Transcript.CreatedFromCaptureId`,
`CaptureProvenanceV1.CaptureItemId`, and every proposal/provenance reference keep resolving. New
captures dual-write until Inbox reads no longer depend on `LlmRequest.Payload`; `LlmRequest` is then
retained only as a provider-call/legacy queue record. This supersedes ADR-0005 when it ships.

### 2. Input dimensions are separate fields

Persist independently: **modality** (`Text | Audio | Image | Document | Structured`), **origin
adapter** (web composer, share target, browser/VS Code extension, MCP, import, integration),
**producer** (`Human | Agent | Integration | Import`), and **intent** (`Remember | Organize | Act |
Auto`). `CaptureSource` remains as a derived compatibility field for existing API clients and is
not extended as the routing model. `Remember` maps onto the shipped `Kept` disposition (a capture
that is preserved and never triaged); `Act` is today's proposal-requested path.

### 3. Derived content is an explicit, immutable Representation

Transcripts, normalised text, OCR output, image descriptions, document structure, and structured
imports become `Representation` records (kind, parent source/representation, processing run,
processor/model/configuration identity, schema version, content hash, language, quality state,
warnings) with **typed payload tables**, not one JSON blob. The shipped `Transcript` becomes the
transcript payload and `ArtefactExtraction` the text payload behind an `IRepresentationStore`
façade; no destructive migration is required. Transcript segments gain optional start/end
milliseconds and char spans *additively*; the line-indexed shape stays valid.

### 4. One evidence vocabulary: EvidenceAnchor

`EvidenceAnchor` kinds: `TextSpan`, `TimeRange`, `PageRegion`, `ImageRegion`, `JsonPointer`,
`WholeSource`, each validated for its permitted fields (char offsets; milliseconds; page number;
normalised rectangle; pointer). Existing Transcript spans are `TextSpan` anchors over the transcript
representation; `ProvenanceEvidenceLink` gains a typed anchor reference, and its string-only
`SourceType`/`SourceId` pair is retired incrementally. A field can then cite exact characters in a
note, `04:18.200–04:29.900` of a recording, a rectangle in a screenshot, or a region on page 7 —
and the UI can highlight, play, or open exactly that.

### 5. Understanding is persisted before mutation: SemanticCandidate

Extraction produces schema-validated `SemanticCandidate` records — `Action | Decision | Question |
Risk | Fact | Reference` — with statement, structured fields, field-level evidence anchors,
derivation kind (extractive vs inferred), optional confidence where honestly available, context
bindings, and state (`Proposed | Corrected | Accepted | Dismissed | Superseded`). A separate
**candidate-to-work compiler** turns Action candidates into the *existing* proposal operations.
Candidate kinds are a different axis from ADR-0060 item types (`Task | Epic | Spike`): a Decision
candidate is a record worth keeping, not a card, until a later slice defines what, if anything, it
compiles to. The candidate layer may be entirely hidden in the minimal presentation; it is an
architectural boundary, not a mandatory screen. Acceptance for the first candidate slice is
**byte-for-byte equivalent proposal operations** for today's transcript golden path.

### 6. Processing is capability-based

Pipelines request **capabilities** (`content.inspect`, `text.normalize`,
`document.extract-text`, `image.ocr`, `image.describe`, `audio.preprocess`, `audio.transcribe`,
`audio.align`, `audio.diarize`, `semantic.extract`, `context.resolve`, `change.plan`,
`change.verify`); they do not select workers by capture-source enum or request-type prefix. A
`ProcessingJob` (capability, input representations, policy snapshot, priority, lease, attempt,
idempotency key, cost ceiling, deadline) and a `ProcessingRun` (processor/model/configuration
identity, usage, latency, route decision and rejected alternatives) separate machinery from user
data. Processors declare a **manifest** (id, version, capabilities, execution `in-process | sidecar
| remote`, locality, accepted media types, languages, features, resources, privacy/egress class,
cost model, output schemas — `processor-manifest.v1` in the archived pack) and register in a
capability registry that also tracks health and installed models. The transcript lane of ADR-0045
becomes the first `semantic.extract` implementation behind this runner; its worker isolation
survives as a scheduling property, not as SQL predicates.

**Router v1 is deliberately simple:** hard constraints (egress class allowed by the processing
profile, media/language support, required features, deadline, budget, device capability, processor
health) followed by the profile's *ordered preference* and a persisted route receipt. Utility
scoring by measured quality/cost/latency is admitted only after the benchmark corpus in §Decision 8
exists — routing is changed by measurements, never by defaults an agent guessed.

### 7. Cascaded, cached processing is the default

Cheap/local/deterministic first; specialised local or inexpensive API when insufficient; a stronger
model only for uncertain or consequential parts. Results are cached by `(input content hash,
processor id/version, model snapshot, normalised configuration hash, output schema version)` so a
rerun never re-bills a provider unless the user asks for a fresh run or a different processor.

### 8. Measurement precedes adaptivity

A modality benchmark corpus (speech WER/DER/alignment error; OCR accuracy and escalation rate;
candidate precision/recall by kind; target accuracy; unchanged-acceptance rate; correction
distance; false-action rate; capture-to-accepted-change latency; **cost per accepted change**;
local-vs-cloud ratio; egress events) is built before any adaptive routing, learned weight, or
"auto" behaviour is allowed to change what a user sees. The shipped outcome/feedback/usage records
are the substrate.

### 9. Change control stays separate — and review-first stays shipped

Processors, LLMs, integrations, and agents create representations and candidates; **none of them
has a tool that mutates work state**. Candidates compile into proposal operations; the policy
engine — never the proposer — evaluates them and records an `AuthorityDecision`
(`Block | Review | InlineConfirm | Authorize`; authority type `Human | NamedPolicy`; policy
version; reason codes; evaluated facts); the executor applies exactly the approved bundle and emits
an `ExecutionReceipt`. This is ADR-0057's shape. Its only shipped policy remains **Review** (explicit
approve, then explicit execute) until a delegated-authority slice is separately gated on review
acceptance/correction evidence; the MCP surface keeps no approve/apply tool. Minimal UX comes first
from grouping, defaults, context resolution, and receipts — not from broader auto-apply.

### 10. One product, modular monolith, supervised sidecars

Taskdeck stays one product with progressive disclosure, one domain, and one deployable by default.
Local ML (transcription, alignment, diarisation, OCR) runs in **supervised sidecars** speaking the
*Taskdeck Worker Protocol v1* (JSON-RPC over stdio: `processor.run`, `processor.progress`, typed
result/error envelopes, per-process session secret, spool-directory inputs, protocol-only stdout,
content-free stderr, deadlines and output caps, network denial for local-only manifests). The
memory-capped extraction worker process of ADR-0048 (`#1429`) is the first host of that protocol,
so the sidecar supervisor is proven on PdfPig before any Python/CUDA process. Hosted CPU/GPU workers
may later deploy behind the same contracts; no microservice split now.

### 11. Storage stays portable through `IBlobStore`

Locally, `IBlobStore` is SQLite-backed (`ArtefactBlob` becomes its implementation), preserving the
single-file ownership promise of ADR-0046 decision 4 and REVIVAL_PLAN §2. Hosted installations may
implement it over object storage **only** at ADR-0061 stage 3 (managed SaaS); stages 1–2 stay
SQLite. Audio raises the per-artefact cap by configuration (a 45-minute meeting at 64 kbps is
~21 MB against today's 10 MB default) and carries an explicit retention policy (keep; keep until
transcript verified; N days; delete after processing) whose evidence-playback trade-off the UI
states. Content-addressed deduplication, streaming reads/writes, quotas, and backup-size reporting
are part of the abstraction, not later extras.

### 12. Boardless capture and understanding are mandatory

No board is required to capture, store, transcribe, OCR, understand, retain, or search. Context
resolution happens at change-planning time, in this order: explicit named target → current UI
context → integration-provided context → exact alias → recent project context → semantic match →
unresolved (stays in Inbox). The resolver records its reason and certainty; low certainty produces
one narrow question or an unresolved capture, never a failed asynchronous job. The same resolver
serves chat (`#2004`). Targets are boards until ADR-0060 stage 4 introduces Project.

### Voice ruling

- Two paths, not one: a **short voice note** (one speaker, minutes, latency-sensitive) routes to a
  lightweight local engine (`whisper.cpp` or `faster-whisper` — spike decides) or an inexpensive
  cloud STT, with no diarisation or forced alignment; a **meeting recording** routes to a diarising
  engine — WhisperX as the local enrichment processor, or a cloud diarising STT — selected by the
  processing profile and hardware.
- The maintainer's existing WhisperX pipeline is wrapped as a sidecar processor; its Python/PyTorch
  internals are never copied into the .NET application, and bundling them in the desktop archive is
  deferred.
- Raw audio is retained by explicit policy; deleting it removes exact playback evidence and the UI
  says so.
- A direct audio-understanding model is ineligible to drive work mutation unless it also emits a
  durable, time-anchored representation.
- This partially supersedes ADR-0033: desktop voice is no longer "prototype only". ADR-0033's
  rejection of `webkitSpeechRecognition` (audio streamed to Google) stands — voice enters as stored
  audio processed under the processing profile, never as a browser-native egress.

### Image ruling

- Local OCR/layout extraction (PaddleOCR-class sidecar, benchmarked on the real screenshot corpus)
  is the default first stage where the device permits; consent-gated cloud vision is the escalation
  for spatial/visual meaning or low-confidence local output. OCR regions and vision descriptions are
  evidence anchors.
- This **amends ADR-0046 decision 5** (cloud-vision-first MVP, local OCR rejected for native
  dependency weight): the sidecar protocol removes the packaging objection, so local OCR is an
  optional sidecar rather than a native dependency of the executable. GEN-03 (`#1317`) still ships
  first, as **one registered processor** behind the registry with its consent and egress
  constraints intact — not as the permanent domain model of image understanding.

### Three independent policy families

- **Processing profile** — egress class, approved providers/regions, local device use, quality vs
  latency, budgets, diarisation/alignment/OCR escalation, retention, language and project
  vocabulary. Presets: *Private*, *Balanced*, *Controlled*, *Expert*.
- **Authority profile** — exactly ADR-0057's presets (*Observe · Suggest · Assist · Operate ·
  Autonomous · Custom*) and safeguards; no parallel vocabulary is introduced.
- **Presentation profile** — *Flow* (receipts only), *Guided* (what was understood, where it goes,
  the proposed changes), *Control* (routes, alternatives, models, budgets, representations, reruns,
  raw operation diff). This must be reconciled with the shipped workspace modes
  (`guided | workbench | agent`, where `agent` is byte-identical to `workbench` — `#1972`) rather
  than added beside them.

They stay independent: cloud processing with manual review, local processing with routine
authority, maximum quality with minimal UI, and cheap processing with expert inspection are all
valid combinations.

## Alternatives Considered

- **One multimodal LLM call per capture** — fuses transcription/OCR, understanding, context
  selection, and planning into one opaque result; loses replay and evidence; pays frontier prices
  for trivial inputs; makes provider substitution a rewrite. Rejected as the default; allowed as an
  optional specialist processor that still emits evidence.
- **Direct audio- or image-to-board mutation** — machine interpretation becomes indistinguishable
  from explicit user action and drops the transcript/time-range evidence. Rejected.
- **One worker and request type per modality** (extending ADR-0045's pattern) — routing, SQL
  predicates, retry behaviour, and observability multiply per input type. Rejected.
- **One generic JSON "content" table** — weak validation, querying, retention, and migrations.
  Rejected in favour of typed metadata plus typed payloads.
- **Graph database now** — relational link tables and anchors cover the expected graph while
  keeping SQLite, EF Core, migrations, and export. Rejected.
- **Microservices now** — the product needs module and processor contracts, not distributed-systems
  overhead; sidecars and later independently deployed workers suffice. Rejected.
- **A second, simpler generalist product** — already rejected by ADR-0046; presentation profiles
  over one engine replace it.
- **Cloud-only or local-only processing as the universal default** — conflicts with P7 and with the
  cost evidence in the pack (an always-on GPU costs ~$550/month before the first minute; managed STT
  at low volume costs cents per hour). Rejected in favour of profile-constrained adaptive routing.
- **Full utility-scored routing from day one** — no corpus to score against; would encode guessed
  weights as behaviour. Deferred behind §Decision 8.

## Consequences

Positive: one architecture for text, voice, images, documents, connectors, and agents; capture
works offline and survives processor failure; processors and providers become replaceable and
benchmarkable; cost, quality, privacy, and latency become independently personalisable; the
proposal/review machinery stays central and gains typed evidence across modalities; project-manager
outputs (decisions, questions, risks) become first-class without turning every sentence into a
card; the transcript and artefact work already shipped is reused through adapters rather than
rewritten.

Costs: a new Capture aggregate and ID-preserving backfill; jobs, runs, representations, anchors,
and candidates as new entities; temporary dual-write and façade complexity; a benchmark corpus and
routing receipts before any adaptive behaviour; explicit retention and storage policy; a sidecar
supervisor and conformance suite.

Risks and mitigations: premature generic abstraction (bounded schemas, typed capabilities, the
transcript lane as the first and only consumer of each seam until a second exists); the candidate
layer becoming visible complexity (hidden in Flow, byte-parity acceptance); manifests drifting from
behaviour (conformance tests per processor); routing opacity (route receipts, no scoring without
measurement); hosted/local divergence (one `IBlobStore` contract, object storage only at ADR-0061
stage 3); scope regrowth (the wave is seeded complete under REVIVAL_PLAN §5's queue caps and the
CF-00 tracker; new CF surface needs a plan amendment).

Neutral: `LlmRequest` survives as a provider-call record; `CaptureSource` survives as a derived
field; `TranscriptTriageWorker` survives as a scheduling lane behind the capability runner.

## Compatibility plan (bounded slices; each maps to CF-NN issues)

0. **Ratify and measure** — this ADR, terminology, benchmark fixtures, cost/latency metric schema.
   No behaviour change.
1. **Durable Capture** — table, ID-preserving backfill, dual-write, `ICaptureStore` façade for
   Inbox reads, export/delete/import and rollback proof.
2. **Jobs and runs** — `ProcessingJob`/`ProcessingRun`; the capability runner; the transcript lane
   adapted behind it; `LlmRequest.Payload` stops being mutable capture state.
3. **Representations and anchors** — `IRepresentationStore` over `Transcript` and
   `ArtefactExtraction`; `EvidenceAnchor`; additive segment timing.
4. **Semantic candidates** — persisted kinds; transcript triage emits candidates and compiles them
   to byte-identical proposals; corrections and supersession.
5. **Processor registry and routing** — manifests, conformance suite, the ADR-0048 worker as the
   first sidecar host, WhisperX and OCR sidecars, one cloud STT adapter, processing profiles, route
   receipts, cache.
6. **Universal Capture and boardless context resolution** — one progressive composer (text, paste,
   voice, image, file), optional context, intent modes, capture-centred Review and receipts.
7. **Delegated authority, first slice** — one narrow reversible operation class under a named
   policy, separately gated on review evidence (ADR-0057).
8. **Hosted scale** — object-store `IBlobStore`, durable hosted queue, scale-to-zero GPU workers —
   only under ADR-0061 stage 3.

The first credible vertical reuses the shipped engine end to end: *real Capture table → audio
SourceAsset → WhisperX through the worker protocol → transcript representation → existing transcript
understanding → existing proposal compiler → voice capture UI → exact audio evidence playback.* It
must **not** be combined with the canonical WorkItem migration, delegated autonomy, every
image/document processor, microservice decomposition, adaptive provider learning, or team approval
chains.

## Acceptance conditions

This ADR moves from Proposed to Accepted only when the maintainer has recorded, on tracker CF-00:

1. terminology and the public product line (§Decision preamble);
2. the release placement of the wave — whether the behaviour-preserving foundation slices (0–3)
   join v0.4 beside the hosted beta, or the whole wave follows as its own release;
3. whether `SemanticCandidate` is persisted (§5) or remains an in-memory compiler stage for now;
4. the storage ruling — `IBlobStore` with SQLite locally (§11) versus keeping `ArtefactBlob` as
   the only physical store until hosted stage 3 forces the abstraction;
5. the default processing profile for a fresh install (*Balanced*, or *Private* with explicit
   opt-in to cloud);
6. the first delegated-authority operation class and the evidence bar that opens it (§9);
7. ID-preserving backfill (§1) versus fresh IDs with a mapping table;
8. the ADR-0046 relationship — amended in place (decisions 4 and 5) versus partially superseded;
9. the presentation-profile reconciliation with workspace modes (`#1972`).

## References

- `docs/analysis/2026-08-30-context-fabric/RECONCILIATION.md` — pack-versus-repository
  verification, the re-think, the release-ladder options, and the issue map
- `docs/analysis/2026-08-30-context-fabric/TASKDECK_CONTEXT_FABRIC_BLUEPRINT.md` — the pack's
  product and architecture blueprint (as received, dated 2026-08-30)
- `docs/analysis/2026-08-30-context-fabric/TASKDECK_WORKER_PROTOCOL_POC.md`,
  `processor-manifest.schema.json`, `whisperx-processor.example.json` — the worker protocol proof
  of concept and manifest contract
- `docs/REVIVAL_PLAN.md` §4 Phase 5 and §7 — the wave and its (pending) new-surface authority
- `docs/strategy/PRODUCT_DIRECTION.md` §1, §5, §7 — engine statement, ladder proposal, open decision
- ADR-0005, ADR-0033, ADR-0045, ADR-0046, ADR-0047, ADR-0048, ADR-0057, ADR-0060, ADR-0061
- Issues: `#1276` (voice re-ignition, RC deck q-9), `#1317`/`#1318`/`#1320`/`#1323` (GEN-03/04/06/09),
  `#1327` (GEN-00), `#1429` (ADR-0048 worker), `#2004`, `#2089`, `#2141`, `#1972`
