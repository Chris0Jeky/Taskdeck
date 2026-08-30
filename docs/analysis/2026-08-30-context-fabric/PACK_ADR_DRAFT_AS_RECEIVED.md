# ADR-0063: Context Fabric — Durable Capture, Derived Representations, Semantic Candidates, and Capability-Based Processing

- **Status:** Proposed
- **Date:** 2026-08-30
- **Deciders:** Maintainer
- **Related:** ADR-0003, ADR-0046, ADR-0057, ADR-0060; issues #1304, #1315–#1323, #2141

## Context

Taskdeck’s product direction identifies context-to-action as its engine. Current implementation has strong but separate components:

- capture rows encoded as `LlmRequest` plus `CapturePayloadV1`;
- durable transcripts and transcript triage;
- immutable source artefacts, blobs, and extraction history;
- proposal/review/apply, field provenance, and transcript evidence;
- future delegated authority and canonical-work decisions.

This architecture proved the transcript wedge, but it does not scale cleanly to text, audio, images, files, connectors, and agents. `LlmRequest` is both durable user-facing capture storage and a disposable process job. `CaptureSource` mixes modality, origin, transport, and producer. Transcript routing is tied to request-type strings and dedicated queue predicates. Artefact text is planned to enter by reusing the transcript lane, which generalizes the implementation only to “text that resembles a transcript,” not to arbitrary source/representation graphs.

The product must optimize first for text/notes and voice, then images, while supporting local, hosted, cloud-provider, team, and high-control use without separate products.

## Decision

### 1. Adopt the Context Fabric pipeline

```text
Capture → SourceAsset → Representation → SemanticCandidate
        → ContextBinding → ChangeSet → AuthorityDecision
        → ExecutionReceipt
```

Every derived or mutating object is attributable to its input, processor, policy, and outcome.

### 2. Capture becomes a durable aggregate

A `Capture` is a user-owned Inbox object independent of processing jobs. It may contain multiple immutable `SourceAsset` objects. A capture remains readable and actionable when processing is unavailable or permanently fails.

Existing capture IDs should be preserved during migration where practical. New paths dual-write until Inbox reads no longer depend on `LlmRequest.Payload`.

### 3. Separate input dimensions

Do not add further overloaded `CaptureSource` values as the primary routing model. Persist independently:

- modality;
- origin adapter/transport;
- producer principal/kind;
- user intent mode;
- explicit context hints.

### 4. Derived content is represented explicitly

Transcripts, normalized text, OCR output, image descriptions, and structured imports are immutable `Representation` records. Current `Transcript` and `ArtefactExtraction` are adapted behind `IRepresentationStore` before any destructive schema convergence.

### 5. Generalize evidence

Introduce typed `EvidenceAnchor` locations over immutable representations: text spans, audio time ranges, image/PDF regions, JSON pointers, and whole-source anchors. Replace string-only evidence references incrementally with typed foreign keys/link tables.

### 6. Persist semantic candidates

Understanding produces schema-validated `SemanticCandidate` records—Action, Decision, Question, Risk, Fact, or Reference—before compiling board/work mutations. Candidates retain field evidence, corrections, context bindings, and supersession.

The minimal UI may hide this layer; it is an architectural boundary, not a mandatory workflow step.

### 7. Processing is capability-based

Introduce `ProcessingJob`, `ProcessingRun`, processor manifests, and a capability registry. Pipelines request capabilities such as `audio.transcribe`, `image.ocr`, and `semantic.extract`; they do not select workers by capture-source enum or request-type prefix.

The planner applies hard privacy/security/feature constraints and then scores eligible processors using measured quality, cost, latency, reliability, and device availability.

### 8. Use cascaded routing

Default routing is local/deterministic or low-cost first, with escalation only for unsupported, low-confidence, high-consequence, or user-selected quality cases. Processor results are cached by content hash, processor/model/version, configuration hash, and output schema.

### 9. Keep change control separate

Processors may create representations and candidates but cannot mutate work state. Candidates compile into existing proposal/change-set operations. A separate policy engine evaluates delegated authority; a separate executor applies the approved bundle and emits a receipt.

### 10. Keep one product and modular-monolith domain

Taskdeck remains one product with presentation profiles and progressive disclosure. Logical modules remain in one deployable application by default. Local ML runs through supervised sidecars. Hosted CPU/GPU workers may deploy separately behind the same contracts when justified.

### 11. Storage remains portable through an abstraction

Local storage keeps SQLite-backed blobs and the single-file ownership story through `IBlobStore`. Hosted installations may use object storage through the same contract. Metadata, lineage, policy, and export semantics are common.

### 12. Boardless capture is mandatory

Capture, source persistence, representation generation, and semantic understanding do not require a board. Context binding may resolve a project/board later. Low-confidence resolution leaves the capture in Inbox or asks one narrow question.

## Voice-specific ruling

- Short voice notes use a lightweight local or inexpensive cloud transcription route.
- WhisperX is an optional local processor for alignment, diarization, and high-evidence meeting workflows, not the only audio path.
- Raw audio is retained according to explicit policy.
- A direct audio-understanding model is ineligible for mutation unless it emits durable inspectable evidence and an accountable representation.

## Image-specific ruling

- Local OCR/layout extraction is the default first stage when device capability permits.
- Consent-gated cloud vision is an escalation for complex visual semantics or low-confidence local extraction.
- OCR/vision regions are evidence anchors.

This amends ADR-0046’s cloud-vision-first MVP recommendation while preserving its consent and egress constraints.

## Consequences

### Positive

- one architecture for text, voice, images, documents, connectors, and agents;
- capture works offline and survives processor failure;
- processors and providers become replaceable and benchmarkable;
- cost, quality, privacy, and latency can be personalized independently;
- proposal trust machinery remains central;
- serious project-management outputs can include decisions, questions, and risks, not only tasks;
- existing transcript/artefact work is reused through adapters.

### Costs

- a new Capture aggregate and migration path;
- additional entities for jobs, runs, candidates, and evidence anchors;
- temporary compatibility adapters and dual-write complexity;
- evaluation corpus and routing metrics are required before adaptive decisions can be trusted;
- more explicit retention and storage policy.

### Risks

- premature generic abstractions;
- candidate layer becoming visible complexity;
- processor manifests drifting from actual behavior;
- routing opacity;
- hosted/local behavior divergence.

Mitigations: bounded schemas, typed capabilities, conformance tests, routing receipts, one default profile, and staged migration.

## Compatibility plan

1. Add façade interfaces over current captures, transcripts, artefacts, and extraction records.
2. Add Capture and dual-write without altering current API behavior.
3. Add ProcessingJob/Run and adapt transcript processing.
4. Add Representation/EvidenceAnchor compatibility.
5. Add candidates and compile them back to existing proposals.
6. Migrate UI and APIs.
7. Retire capture state stored in `LlmRequest.Payload` only after export, migration, and rollback proof.

## Alternatives rejected

- one giant multimodal model call;
- direct audio/image-to-board mutation;
- a worker/request type per modality;
- immediate microservices;
- immediate graph database;
- a second generalist product;
- cloud-only or local-only processing as the universal default.

## Acceptance conditions

This ADR is complete when the maintainer has ruled on:

- the terminology and public product promise;
- whether SemanticCandidate is persisted;
- local blob abstraction versus SQLite-only physical storage;
- default processing profile;
- first delegated-authority operation class;
- compatibility/backfill ID strategy;
- whether ADR-0046 is amended or partially superseded.

