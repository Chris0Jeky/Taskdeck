# Taskdeck Context Fabric

## Product and architecture blueprint

**Prepared:** 30 August 2026  
**Repository baseline reviewed:** `Chris0Jeky/Taskdeck` at `2807c0b5c3ed9d17b7c09feb3121c6f09a0ee7dd`

---

## Executive decision

The “anything enters” thesis is strong, but it should not be implemented as a growing list of capture types feeding bespoke workers. The strongest form is a **context-to-change fabric**:

> **Speak, type, paste, or drop. Taskdeck preserves the source, derives an accountable understanding, resolves it against project context, and changes work only under rules the user chose.**

Internally, the invariant is:

```text
Capture → Source → Representation → Candidate → Context binding
        → Change set → Authority decision → Execution → Receipt
```

This is more rigorous than “voice-to-task” and more useful than “AI task manager.” It separates five concerns that are currently too close together:

1. **What entered**: immutable source material.
2. **What Taskdeck derived**: transcription, OCR, normalized text, semantic candidates.
3. **Where it belongs**: workspace, project, board, work item, person, or inbox.
4. **What should change**: a typed operation bundle with preconditions.
5. **Who allowed the change**: human confirmation or a named delegated-authority policy.

The architecture should remain a **modular monolith**. Local deployments run the API, SQLite, and lightweight processors together, with optional local ML sidecars. Hosted deployments may place CPU/GPU workers behind a queue, but that is a deployment choice, not a different domain model.

---

## 1. What should be preserved from Taskdeck

Taskdeck already contains most of the hard trust substrate:

- immutable `SourceArtefact` metadata and a separate `ArtefactBlob` table;
- append-only `ArtefactExtraction` records;
- durable `Transcript` records;
- proposal operations, revisions, preview/apply discipline, risk classification, outcomes, and receipts;
- field-level provenance and transcript evidence spans;
- a local-first SQLite posture;
- a future delegated-authority model that preserves separation of duties;
- an accepted canonical-work direction that can eventually separate stable work identity from board placement.

These are not throwaway experiments. They should become modules behind a more general model.

The central structural debt is that **capture is currently persisted through `LlmRequest` and a serialized `CapturePayloadV1`**. A user-owned inbox item and a disposable processing job are different lifecycle objects. Keeping them fused will make every new modality, retry mode, processor, and offline path harder.

A second debt is that modality, source, and transport are mixed together. `CaptureSource` currently includes concepts such as `Voice`, `TranscriptFile`, `ShareTarget`, and `VsCodeExtension`. These describe different dimensions:

- modality: text, audio, image, document;
- transport: web composer, file upload, share sheet, browser extension;
- producer: human, meeting connector, agent;
- semantic intent: remember, organize, act.

Those dimensions should become separate fields.

---

## 2. Product thesis

### Public promise

**Speak, type, paste, or drop. Taskdeck turns context into accountable work, under your rules.**

Use “anything enters” as an architecture doctrine, not a literal launch claim. Literal “anything” creates an expectation that every file, integration, and ambiguous media type is equally understood. Public copy should name the optimized paths:

> **Capture thoughts, notes, screenshots, and voice. Taskdeck organizes them, proposes the right changes, and shows what happened.**

### Internal product model

Taskdeck is not primarily a board. The board is a work surface. Taskdeck’s core product is the controlled conversion of **context into movement**:

```text
raw context
    ↓
inspectable understanding
    ↓
project-aware intent
    ↓
accountable state transition
```

### The differentiated capability

Most capture products stop at transcription, summary, or extracted action items. The stronger Taskdeck loop is:

- preserve source evidence;
- distinguish extractive facts from inference;
- resolve the target against live project state;
- generate typed, previewable operations;
- apply only under explicit authority;
- retain the outcome and execution receipt.

That makes Taskdeck useful to an individual with a shopping note and to a project manager processing a multi-speaker review meeting without requiring two products.

---

## 3. The target architecture

### 3.1 Logical modules

```text
┌─────────────────────────────────────────────────────────────────────┐
│                           Experience layer                          │
│ Universal Capture · Inbox · Review · Boards/Views · Activity       │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────────┐
│                              Intake                                │
│ Capture envelope · Source assets · origin · retention · context hint│
└───────────────────────────────┬─────────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────────┐
│                        Processing fabric                           │
│ Planner · jobs · capability router · processors · budgets · cache  │
└───────────────┬──────────────────────────────┬──────────────────────┘
                │                              │
┌───────────────▼──────────────┐ ┌─────────────▼─────────────────────┐
│ Representations & evidence   │ │ Security, privacy, egress         │
│ text · transcript · OCR      │ │ scanning · consent · residency    │
│ segments · regions · anchors │ │ injection rails · retention       │
└───────────────┬──────────────┘ └───────────────────────────────────┘
                │
┌───────────────▼─────────────────────────────────────────────────────┐
│                      Understanding and context                      │
│ candidates · entity resolution · project binding · conflict checks │
└───────────────┬─────────────────────────────────────────────────────┘
                │
┌───────────────▼─────────────────────────────────────────────────────┐
│                         Change control                              │
│ change sets/proposals · policy evaluation · execution · receipts   │
└───────────────┬─────────────────────────────────────────────────────┘
                │
┌───────────────▼─────────────────────────────────────────────────────┐
│                          Work domain                                │
│ workspace · project · work item · board/view · people · relations  │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 Physical deployment

**Desktop/local**

```text
Taskdeck API + Vue UI + SQLite
        ├── in-process deterministic processors
        │     text normalization, PDF text layer, simple parsing
        ├── optional supervised local worker
        │     faster-whisper / WhisperX / PaddleOCR
        └── optional remote provider adapters
              speech-to-text, vision, text LLM
```

**Hosted**

```text
Taskdeck modular monolith
        ├── relational metadata store
        ├── object/blob store through IBlobStore
        ├── durable processing queue
        ├── autoscaling CPU workers
        └── scale-to-zero GPU workers / remote provider adapters
```

Do not start with microservices. Define module contracts and an outbox now; split worker deployment only when latency, compute isolation, or scale makes it necessary.

---

## 4. Canonical data model

### 4.1 Capture: the durable user-facing root

A `Capture` is the thing the user sees in Inbox. It remains valid even if every processor fails.

```text
Capture
- Id
- OwnerPrincipalId
- CapturedAtServer
- CapturedAtClient?
- OriginAdapterId
- ProducerKind            Human | Agent | Integration | Import
- IntentMode              Remember | Organize | Act | Infer
- ExplicitContextHint?    current board/project/work item
- RetentionPolicyId
- LifecycleState
- UserTitle?
- UserNote?
```

A capture may contain multiple sources: a typed sentence plus a screenshot, or a voice note plus a pasted link. This avoids pretending one user action always equals one file or one text payload.

### 4.2 SourceAsset: immutable raw truth

```text
SourceAsset
- Id
- CaptureId
- Modality                Text | Audio | Image | Document | Structured
- MediaType
- ContentHash
- ByteSize
- BlobReference / inline-text reference
- OriginalName?
- ExternalReference?
- SecurityState
- MetadataJson            bounded, schema-versioned
```

Raw assets never change. A correction creates a new source or a user-authored representation, preserving history.

`SourceArtefact` can evolve into this entity. Existing `ArtefactBlob` remains a valid local blob implementation.

### 4.3 Representation: every derived view of a source

A representation is immutable and reproducible from its parent plus a processor/version/configuration.

```text
Representation
- Id
- CaptureId
- Kind                    NormalizedText | Transcript | OCRText |
                          ImageDescription | DocumentStructure |
                          StructuredEvent
- ParentSourceAssetId?
- ParentRepresentationId?
- ProcessingRunId
- SchemaVersion
- ContentHash
- Language?
- QualityState
- Warnings
- CreatedAt
```

Use typed payload tables rather than one unbounded JSON blob:

- `TextRepresentation`
- `TranscriptRepresentation`
- `TranscriptSegment`
- optional `TranscriptWord` only when word alignment exists
- `DocumentPage`
- `TextRegion`
- `ImageDescription`

Current `Transcript` and `ArtefactExtraction` should initially be exposed through an `IRepresentationStore` compatibility layer. They do not need an immediate destructive migration.

### 4.4 EvidenceAnchor: one evidence vocabulary for every modality

Current transcript-only span support should generalize to a typed anchor:

```text
EvidenceAnchor
- Id
- RepresentationId
- Kind                    TextSpan | TimeRange | PageRegion |
                          ImageRegion | JsonPointer | WholeSource
- CharStart? / CharEnd?
- StartMilliseconds? / EndMilliseconds?
- PageNumber?
- X? / Y? / Width? / Height?    normalized 0..1
- JsonPointer?
- QuoteHash?
```

Validation enforces the fields allowed for each anchor kind. A task can then cite:

- exact characters in a note;
- 04:18.200–04:29.900 in audio;
- a rectangle in a screenshot;
- page 7, region 0.1/0.2/0.7/0.15 in a PDF.

This is the basis for trustworthy playback, highlighting, and re-verification.

### 4.5 SemanticCandidate: understanding before mutation

Do not force every extraction directly into a proposal. Persist an intermediate semantic object:

```text
SemanticCandidate
- Id
- CaptureId
- Kind                    Action | Decision | Question | Risk |
                          Fact | Reference
- Statement
- StructuredFields       schema-versioned, validated
- State                   Proposed | Corrected | Accepted |
                          Dismissed | Superseded
- CreatedByProcessingRunId
- CandidateRevisionId?
```

Supporting links:

- `CandidateEvidence(CandidateId, EvidenceAnchorId, FieldName, DerivationKind, Confidence?)`
- `CandidateRelation(FromCandidateId, ToCandidateId, RelationKind)`
- `CandidateContextBinding(CandidateId, TargetType, TargetId, Confidence, Reason, ConfirmedBy?)`

Why this layer matters:

- a voice note can contain a decision worth preserving but no board mutation;
- one source can produce many candidates;
- several captures can support one change;
- users can correct understanding before planning operations;
- model reruns do not duplicate cards;
- project managers gain decisions, questions, and risks rather than a flat pile of tasks;
- accepted corrections become evaluation data without covertly “training on the user.”

The layer can be hidden completely in the minimal UI.

### 4.6 ChangeSet: the existing proposal machinery, generalized

The existing `AutomationProposal` is close to the required `ChangeSet`:

```text
ChangeSet
- requested operations
- target preconditions and concurrency tokens
- risk and reversibility classification
- diff preview
- source candidates and evidence
- proposer principal
- policy evaluation status
- approved revision
- execution state
```

Do not allow processors, LLMs, integrations, or agents to write work state directly. They produce candidates or change sets. The policy engine and execution service remain separate actors.

### 4.7 AuthorityDecision and ExecutionReceipt

```text
AuthorityDecision
- ChangeSetId
- Decision                Block | Review | InlineConfirm | Authorize
- AuthorityType           Human | NamedPolicy
- AuthorityId
- PolicyVersion
- ReasonCodes
- EvaluatedFacts
- ExpiresAt?

ExecutionReceipt
- ChangeSetId
- ExecutedOperations
- PreconditionsObserved
- Result
- CompensatingAction?
- StateVersionBefore/After
- ExecutedAt
```

This implements the already accepted user-sovereign direction without making “autonomy” a magic toggle.

---

## 5. Processing fabric

### 5.1 Replace request-type routing with capability planning

The current transcript pipeline dispatches by string request type and dedicated SQL predicates. That is appropriate for one expensive lane, but brittle as modalities multiply.

The new orchestration vocabulary should be capability-based:

```text
content.inspect
text.normalize
document.extract-text
image.ocr
image.describe
audio.preprocess
audio.transcribe
audio.align
audio.diarize
semantic.extract
context.resolve
change.plan
change.verify
```

A `PipelinePlanner` creates a small DAG for each capture. A `ProcessingJob` asks for a capability; it does not name a hard-coded worker class.

```text
ProcessingJob
- Id
- CaptureId
- Capability
- InputRepresentationIds
- PolicySnapshotId
- PriorityClass
- State
- LeaseOwner / LeaseExpiresAt
- Attempt
- IdempotencyKey
- MaxCost
- Deadline
```

`LlmRequest` can remain temporarily as a provider-call/legacy queue record, but it should stop being the Capture aggregate.

### 5.2 Processor registry

Every processor declares a manifest:

```json
{
  "id": "taskdeck.whisperx",
  "version": "1.0.0",
  "capabilities": ["audio.transcribe", "audio.align", "audio.diarize"],
  "execution": "sidecar",
  "locality": "local",
  "accepts": ["audio/*", "video/mp4"],
  "resources": { "cpu": true, "gpu": "optional", "minVramMb": 0 },
  "features": ["word-timestamps", "speaker-labels", "vad"],
  "privacy": { "networkRequired": false },
  "costModel": { "type": "compute-time" }
}
```

The registry contains live health, benchmark measurements, installed models, supported languages, and queue pressure. “First extractor whose MIME matches” becomes a compatibility strategy, not the final router.

### 5.3 Hard constraints before scoring

The router first removes processors that violate policy:

- local-only or approved-egress destinations;
- data residency;
- media and language support;
- maximum cost;
- latency deadline;
- available CPU/GPU/VRAM;
- team compliance policy;
- required features such as diarization or word timestamps;
- processor health and circuit-breaker state.

Only eligible processors are scored.

### 5.4 Utility scoring

A route is selected from measured attributes, not brand preferences:

```text
utility = quality_weight × expected_quality
        - cost_weight    × expected_cost
        - latency_weight × expected_latency
        - privacy_weight × egress_penalty
        - reliability_penalty
        - energy/compute penalty
```

The weights come from a processing profile. The decision and alternatives are recorded on `ProcessingRun` so routing never becomes invisible.

### 5.5 Cascaded processing

The strongest cost/quality pattern is **cheap first, escalate only when needed**:

```text
local deterministic / small model
        ↓ if unsupported, low-confidence, or policy requires more
specialized local model or inexpensive API
        ↓ only for ambiguous spans / complex layout / high consequence
higher-quality multimodal or reasoning model
```

Examples:

- OCR a screenshot locally, then call a vision model only when layout or non-text visual meaning matters.
- Transcribe a short single-speaker note without diarization or forced alignment.
- Reprocess only low-confidence audio spans with a second model.
- Run expensive context resolution only after cheap exact-name and current-context matches fail.

### 5.6 Idempotency and cache

A processing result should be reusable when this tuple matches:

```text
input content hash
+ processor id/version
+ model snapshot
+ normalized configuration hash
+ output schema version
```

This prevents repeated billing and makes reproducibility concrete. A user can deliberately request a fresh run or a different processor.

---

## 6. Modality-specific pipelines

## 6.1 Text and notes: the primary path

Text should be the fastest and least theatrical route:

```text
raw text
→ normalize
→ deterministic hints and explicit command parsing
→ semantic extraction only if useful
→ context resolution
→ change planning
```

Recommendations:

1. **Preserve the exact raw text** and create normalized text as a representation.
2. Recognize explicit commands separately from inferred commitments.
3. Use deterministic parsing for obvious dates, board names, labels, and direct command grammar.
4. Use a small language model for ambiguity and semantic decomposition.
5. Permit “remember only” notes that never become proposals.
6. Let several short captures accumulate and be organized later.
7. Use project vocabulary, known people, board names, and recent activity as bounded context—not a blind dump of the whole workspace.

An explicit user command such as “move release checklist to Done” carries stronger intent than “we should probably finish the checklist.” Both are machine-interpreted, but policy can treat them differently.

## 6.2 Voice: optimize two different jobs

“Voice” is not one workload.

### A. Short voice note / thought capture

Target: immediate capture, usually one speaker, usually under five minutes.

```text
record locally
→ VAD / resample / normalize
→ fast local or inexpensive cloud STT
→ provisional transcript visible quickly
→ semantic extraction
→ context resolution
→ receipt or review
```

Do **not** run full WhisperX alignment and diarization by default. They add compute and dependencies without helping a one-person memo.

### B. Meeting / multi-speaker record

Target: durable evidence, assignments, decisions, questions, risks.

```text
store raw audio
→ VAD and channel inspection
→ transcription
→ diarization if multi-speaker
→ alignment when evidence playback needs accurate ranges
→ speaker/participant resolution
→ candidates: actions, decisions, questions, risks
→ contextual change set
→ bundle review / policy
```

### Recommended local stack

- `whisper.cpp` or `faster-whisper` for a lightweight local baseline.
- WhisperX as a **quality/enrichment processor**, not the only transcription engine.
- pyannote when local diarization is requested.
- The existing WhisperX pipeline should be wrapped as a processor adapter, not copied into the .NET domain.

### Recommended cloud stack

Provide provider-neutral adapters. Dedicated speech APIs can be materially cheaper than maintaining an always-on GPU at low volume, while a scale-to-zero local/hosted GPU worker can be economical at sustained volume. The router should decide from policy and measured performance.

### Never discard the audio-to-evidence path

A direct audio-capable LLM may be useful for non-verbal events or specialized understanding, but it should not be the default route to work mutations. A valid action path must still emit:

- a durable transcript or equivalent representation;
- evidence time ranges;
- processor/model provenance;
- an inspectable interpretation.

### Audio retention choices

Offer clear policies:

- keep audio indefinitely;
- keep until transcript is verified;
- keep for 7/30/90 days;
- delete immediately after processing;
- legal hold / team retention.

Deleting audio reduces storage and privacy exposure but removes exact playback evidence. The UI should state that tradeoff.

## 6.3 Images and screenshots

Most ordinary image input will be screenshots, photos of notes, whiteboards, receipts, or UI states.

Recommended cascade:

```text
inspect + hash + thumbnail
→ local OCR and layout regions
→ classify whether OCR is sufficient
→ optional consent-gated vision understanding
→ fuse OCR + visual description
→ semantic candidates
```

Use local OCR for text-dominant screenshots. Use vision only when spatial or visual meaning matters, such as:

- a kanban screenshot where position conveys state;
- a chart;
- a UI bug screenshot;
- a handwritten/poor-quality image;
- a diagram or whiteboard.

PaddleOCR-class models are more suitable than making cloud vision mandatory for every image. Keep cloud vision available for quality escalation and low-capability devices.

Every OCR token/region should be anchored to a bounding box when available. Review can then highlight the exact source region.

## 6.4 Documents and integrations

PDF/text extraction already fits the representation model. Later connectors should ingest source assets or structured representations, never bypass change control.

An email, calendar event, GitHub issue, or agent request is not a special mutation path. It is a source with origin metadata and an identity principal.

---

## 7. Cost and performance strategy

### 7.1 Separate three cost categories

1. **Inference cost**: API tokens/minutes or GPU runtime.
2. **platform cost**: queue, storage, egress, model downloads, idle GPU time.
3. **correction cost**: human time caused by poor transcription, wrong extraction, or bad routing.

The cheapest model is not cheapest if it doubles correction time. Optimize for **cost per accepted change**, not cost per minute or token.

### 7.2 Current speech-price anchors

Rates change and must be queried dynamically in production planning. At the time of this blueprint:

- Deepgram lists prerecorded Nova-3 monolingual at **$0.0043/minute**, or about **$0.258 per audio hour**; prerecorded diarization is included.
- AssemblyAI lists high-quality prerecorded speech-to-text around **$0.21/hour**, with lower value tiers around **$0.15/hour**.
- Google Speech-to-Text V2 lists **$0.016/minute**, or **$0.96/hour**, with data-residency and enterprise controls.
- OpenAI’s dedicated transcription models are token-priced; the diarizing model includes speaker diarization, while the mini transcription model halves the listed audio-token input price relative to the larger transcription model.

These prices make the correct default clear: do not operate an idle cloud GPU solely to avoid a sub-dollar transcription bill.

### 7.3 Local/hosted break-even formula

For an on-demand GPU worker:

```text
local compute cost per audio hour
  = GPU price per wall-clock hour / effective real-time factor
  + orchestration, storage, and startup overhead
```

Example assumption—not a market quote:

```text
GPU: $0.75 / wall-clock hour
measured pipeline throughput: 10× realtime
compute: $0.075 / audio hour before overhead
```

That can beat API pricing when the GPU scales to zero and remains well utilized. An always-on worker at the same rate costs roughly `$0.75 × 730 = $547.50/month` before it transcribes anything. At low volume, the idle tax dominates.

### 7.4 Recommended routing defaults

**Balanced default**

- text: deterministic/local first; small remote model only when necessary;
- short voice: local lightweight STT if device score is healthy, otherwise inexpensive cloud STT;
- meetings: cloud diarizing STT by default on ordinary hardware; WhisperX on capable local GPU or privacy profile;
- screenshots: local OCR first, cloud vision on uncertainty or visual-layout requirement;
- semantic extraction: cheap schema-constrained text model, escalate only ambiguous/high-impact candidates;
- no repeated processing when cache keys match.

**Private Local**

- local processors only;
- queue work until a capable device is available;
- no silent cloud fallback;
- reduced feature set is stated honestly.

**Best Quality**

- high-accuracy STT;
- diarization/alignment when useful;
- vision escalation;
- secondary verification for low-confidence/high-consequence fields;
- higher latency/cost accepted within a budget.

**Fastest**

- streaming or low-latency API;
- provisional transcript immediately;
- final post-processing may replace the provisional representation, never rewrite history.

---

## 8. Personalization without a settings wall

Taskdeck needs three independent policy families.

### 8.1 ProcessingProfile

Controls how understanding is produced:

- privacy/egress;
- allowed providers and regions;
- local device use;
- quality/latency preference;
- per-capture and monthly spend limits;
- diarization/alignment/OCR escalation;
- retention;
- language and project vocabulary.

### 8.2 AuthorityProfile

Controls what may change:

- operation allow-list;
- project/board scope;
- source/producer classes;
- evidence requirements;
- target certainty;
- consequence and reversibility ceilings;
- review, inline confirmation, or policy authorization;
- expiry, operation count, and spend budgets;
- kill switch and revocation.

### 8.3 PresentationProfile

Controls how much machinery is visible:

- **Flow**: minimal capture and receipts.
- **Guided**: clear proposed changes and explanations.
- **Control**: processor traces, routing alternatives, budgets, reruns, raw evidence.

Do not tie these together. A user may want cloud processing but manual review, or local processing with broad authority for routine housekeeping.

### 8.4 User-facing presets

A first-run choice can create all three profiles without showing the matrices:

| Preset | Processing | Authority | Presentation |
|---|---|---|---|
| Private | Local only | Ask before interpreted changes | Guided |
| Balanced | Local first, cloud fallback with consent | Routine reversible work can be streamlined; consequential work reviewed | Flow |
| Controlled | Approved providers and residency | Review every machine-interpreted change | Guided |
| Expert | Custom | Custom delegated authority | Control |

The shipped product can retain “review every automation change” until delegated authority is implemented. The model is future-compatible rather than a claim that auto-apply exists now.

---

## 9. Authority model for natural language and voice

A critical distinction:

- **direct human UI mutation**: the user edits a card or drags it;
- **explicit interpreted command**: “Move card X to Done”;
- **inferred commitment**: “We should get X done soon”;
- **agent/integration suggestion**: a non-human actor proposes a change.

All but the first involve machine interpretation. Policy should classify them with these dimensions:

```text
intent strength
source principal
interpretation actor
context certainty
evidence strength
operation consequence
reversibility
external/security/cost effects
```

Possible decisions:

- execute and show a receipt;
- require one inline confirmation;
- place in Review;
- block because policy forbids it.

A minimal experience is therefore not “the AI can mutate everything.” It is “routine, well-grounded, reversible operations happen under a policy I chose, while ambiguity and consequence surface only when needed.”

---

## 10. UX redesign

## 10.1 Universal Capture

One compact surface should accept text, paste, files, images, and voice:

```text
┌────────────────────────────────────────────────────────────┐
│ What happened, what do you need, or what should change?    │
│                                                            │
│  Type or paste…                                            │
│                                                            │
│  🎙 Speak     ＋ Add      Context: Auto ▾     Organize ▾    │
│                                              [Capture]      │
└────────────────────────────────────────────────────────────┘
```

The controls are progressive:

- `Context: Auto` is optional, never a capture blocker.
- `Remember / Organize / Act` sets intent mode.
- provider/processor controls are hidden unless Control mode is active.
- voice shows a live provisional transcript but never loses the raw recording on a transcription failure.

### 10.2 Boardless intake

A board must not be required to preserve or understand a capture. The context resolver can propose a target later.

Priority for context signals:

1. explicit target named by the user;
2. current UI context;
3. source integration context;
4. exact project/board/person match;
5. recent work context;
6. semantic retrieval;
7. inbox unresolved.

The system records why it chose a target. Low certainty asks one narrow question or leaves the item unresolved.

### 10.3 Capture timeline

Every capture has one understandable lifecycle:

```text
Received → Preparing → Understood → Routed
         → Needs review | Acted | Kept | Failed
```

The user sees meaningful state, not worker terminology.

### 10.4 Minimal completion receipt

```text
Done
Added 2 tasks to Taskdeck v0.4
Moved “Image intake spike” to In progress
1 assignment needs you

[Review one item]  [Undo/revert where possible]  [View source]
```

The activity log remains available, but routine use stays calm.

### 10.5 Review grouped by capture

Review should lead with the result, not raw operation machinery:

```text
Meeting capture · 42 minutes · 3 speakers

Taskdeck found
  5 actions   2 decisions   3 questions   1 risk

Proposed project changes
  + 4 tasks
  ↻ 1 due date
  → 2 assignments

[Review changes]
```

Inside review:

- summary first;
- candidate groups second;
- operation diffs third;
- processor/provenance detail on demand;
- click evidence to highlight text, image region, or play the audio range.

### 10.6 Power-user and PM surfaces

Power users can:

- choose or pin a processor;
- compare two representations;
- rerun only one stage;
- inspect latency, cost, model, and warnings;
- define capture hotkeys and default context rules;
- author custom routing and authority policies.

Project managers can:

- map speaker labels to team participants;
- maintain project vocabulary and aliases;
- extract decisions/questions/risks as first-class records;
- review assignment and due-date conflicts;
- apply approval chains and project templates;
- monitor capture-to-action throughput, acceptance, correction, and spend.

The same underlying pipeline supports both; the presentation profile changes what is exposed.

---

## 11. Security and trust boundaries

Every input is untrusted, including pasted text and transcripts.

### Intake boundary

- MIME and magic-byte validation;
- size, duration, page, and decompression limits;
- image downscaling before egress;
- optional malware scanning in hosted/team profiles;
- immutable hashes;
- no user content in ordinary logs.

### Processor boundary

- content framed as data, never instructions;
- extraction processors have no domain mutation tools;
- strict output schemas;
- timeout, memory, CPU/GPU, and network budgets;
- egress allow-list and consent record;
- processor identity, version, model, and configuration recorded;
- sidecars supervised and sandboxed where the platform allows.

### Semantic/change boundary

- candidate evidence retained;
- inferred versus extractive fields distinguished;
- context resolution records reasons and certainty;
- operation preconditions and optimistic concurrency;
- policy engine separate from proposer;
- receipts and compensation/recovery where practical.

Prompt injection is reduced by architecture: source content cannot call tools; it can only produce schema-valid candidates. It is not “solved,” so high-consequence changes still need policy and review controls.

---

## 12. Storage strategy

### Local

Keep the single-file promise through an `IBlobStore` abstraction whose local implementation is SQLite-backed. Continue separating blob rows from hot metadata queries.

Add:

- content-addressed deduplication;
- streaming reads/writes;
- per-modality and per-user quotas;
- retention jobs;
- backup size reporting;
- optional derived-representation eviction and regeneration.

### Hosted

Use the same `IBlobStore` contract with object storage. Metadata, hashes, ownership, retention, and lineage stay in the relational store. Do not force large audio into relational pages merely to keep local and hosted physical storage identical.

### What must always remain portable

A full export contains:

- raw sources when retained;
- representations;
- candidates and corrections;
- change sets, policy decisions, and receipts;
- processor provenance and usage;
- stable IDs and typed evidence anchors.

---

## 13. Evaluation and operational metrics

Build a modality benchmark corpus before auto-routing becomes ambitious.

### Speech

- word error rate by language/noise/device;
- diarization error rate;
- timestamp alignment error;
- named-entity and domain-term accuracy;
- real-time factor, cold start, memory/VRAM;
- cost per audio hour.

### Images/documents

- OCR character/word error rate;
- region/layout accuracy;
- structured table/list recovery;
- escalation rate to vision;
- cost and latency per page/image.

### Context-to-change

- candidate precision/recall by kind;
- due-date, assignee, and target accuracy;
- percentage accepted without edits;
- mean correction distance;
- false-action rate;
- correct “nothing actionable” rate;
- context-routing precision;
- rollback/reversal rate;
- time from capture to accepted change;
- **cost per accepted change**;
- percentage processed locally;
- egress events per user/project.

Provider routing should be changed by measured results on this corpus, not marketing benchmarks.

---

## 14. Migration path from the current repository

Do not rewrite Taskdeck. Introduce compatibility seams and migrate in bounded slices.

### Slice 0 — architecture decision and measurement

- Accept/supersede ADR-0046 portions through a new Context Fabric ADR.
- Define terms: Capture, SourceAsset, Representation, Candidate, ChangeSet.
- Add benchmark fixtures and cost/latency measurement schema.
- Keep all shipped behavior unchanged.

### Slice 1 — make Capture real

- Add a durable `Capture` table.
- Preserve existing capture IDs where feasible by using the `LlmRequest.Id` as the new capture ID during backfill.
- Add origin, producer, intent mode, and lifecycle fields.
- Dual-write new captures to `Capture` plus the legacy queue path.
- Move Inbox reads to the Capture façade.

### Slice 2 — separate jobs from user data

- Add `ProcessingJob` and `ProcessingRun`.
- Queue jobs by capability, not request-type strings.
- Adapt `TranscriptTriageWorker` behind the capability runner.
- Stop using `LlmRequest.Payload` as mutable capture state.
- Retain `LlmRequest` for backward compatibility/provider-call history until migrated.

### Slice 3 — representation and evidence façade

- Add `IRepresentationStore` over existing `Transcript` and `ArtefactExtraction`.
- Add generalized `EvidenceAnchor`.
- Replace string-only source references incrementally with typed FKs/link tables.
- Normalize transcript segments to start/end times and character spans; keep word rows optional.

### Slice 4 — semantic candidates

- Persist Action/Decision/Question/Risk/Fact/Reference candidates.
- Have transcript triage emit candidates first, then compile them into the current proposal format.
- Preserve the current one-step user flow; candidate UI stays hidden initially.
- Add correction and supersession semantics.

### Slice 5 — processor registry and routing profiles

- Introduce processor manifests and capability matching.
- Wrap current plaintext/PdfPig extraction as in-process processors.
- Wrap WhisperX as a local sidecar processor.
- Add one inexpensive remote STT adapter.
- Add routing receipts, budgets, caching, and fallback tests.

### Slice 6 — Universal Capture and boardless context resolution

- Replace separate capture variants with one progressive composer.
- Wire text, voice-file upload/recording, image paste/drop, and text documents.
- Make board/project selection optional.
- Add context suggestions and narrow ambiguity prompts.
- Group Review by capture.

### Slice 7 — delegated authority UX

- Implement authority presets only after evidence from review behavior.
- Start with one narrow reversible class, such as create-card in a chosen board.
- Policy evaluation remains separate from proposer and executor.
- Add kill switch, expiry, budgets, and explicit receipts.

### Slice 8 — hosted scaling

- Introduce object-store blob implementation and durable hosted queue.
- Autoscale CPU/GPU workers; GPU scales to zero.
- Add regional/provider policy and team-level quotas.
- Preserve the local deployment as a first-class product.

---

## 15. How existing v0.4 issues should change

Do not discard the accepted generalist wave. Reframe it:

- **GEN-01 storage**: keep; evolve `SourceArtefact` behind `SourceAsset`/blob interfaces.
- **GEN-02 local extraction**: keep; register PdfPig/plaintext as processors.
- **GEN-03 image extraction**: change from cloud-vision-first to local-OCR-first with cloud vision escalation.
- **GEN-04 artefact triage**: do not route artefacts by pretending they are transcripts. Route representations through `semantic.extract`.
- **GEN-06 Paper intake UX**: widen into Universal Capture and binary offline semantics.
- **GEN-09 injection rails**: keep as a prerequisite, but enforce the stronger processor boundary.
- **REVIVAL M4 voice**: seed as a processor and UX epic, not an audio endpoint wired directly to transcript triage.

The current architecture was correct to generalize the transcript lane rather than fork a product. The next correction is to generalize one level higher—from “transcript-like text” to **sources, representations, and capabilities**.

---

## 16. Decisions recommended now

1. **Adopt Context Fabric as the internal architecture name.**
2. **Keep one Taskdeck product and one work domain.**
3. **Create a real Capture aggregate independent of processing jobs.**
4. **Separate modality, origin, producer, and intent.**
5. **Persist semantic candidates between understanding and mutation.**
6. **Generalize evidence anchors across text, time, page, and image regions.**
7. **Route by capability and policy, not capture-source enums.**
8. **Use local-lightweight → specialized/cloud escalation by default.**
9. **Treat WhisperX as an optional enrichment processor.**
10. **Make boardless/offline capture non-negotiable.**
11. **Keep processing policy, authority policy, and presentation mode independent.**
12. **Preserve modular-monolith architecture; split deployment, not domain, when scale requires it.**
13. **Measure cost per accepted change and correction burden.**
14. **Keep raw evidence whenever retention policy permits.**
15. **Do not market “anything” until the optimized modality list is honestly supported.**

---

## 17. Rejected designs

### One giant multimodal LLM call

Rejected as the default because it fuses transcription/OCR, understanding, context selection, and planning into one opaque result; weakens replay and evidence; makes provider substitution difficult; and pays expensive model costs for simple inputs.

### Direct audio-to-board

Rejected as the default because it makes machine interpretation indistinguishable from explicit user action and often loses transcript/time-range evidence.

### One worker and request type per modality

Rejected because routing logic, SQL predicates, retry behavior, and observability multiply with every input type.

### One generic JSON “content” table

Rejected because it makes validation, querying, retention, and migrations weak. Use a typed base metadata layer plus typed representation payloads.

### Immediate graph database

Rejected. Relational tables with explicit link tables and evidence anchors support the expected graph while preserving SQLite, EF Core, migrations, and portability.

### Immediate microservices

Rejected. The product needs clean module and processor contracts, not distributed-systems overhead. Sidecars and independently deployed workers are enough.

### Separate “simple Taskdeck” product

Rejected unless real user evidence shows incompatible domain needs. Use presentation profiles and progressive disclosure over one engine.

---

## 18. Product language

Recommended public lines:

- **Speak, type, paste, or drop. Taskdeck turns context into accountable work.**
- **Your thoughts and project evidence become the right changes, under rules you choose.**
- **Capture quickly. Understand clearly. Move work forward.**

Recommended internal loop:

> **Capture → Understand → Resolve → Change → Verify**

Recommended architecture term:

> **Taskdeck Context Fabric**

