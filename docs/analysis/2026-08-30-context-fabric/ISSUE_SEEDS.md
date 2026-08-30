# Taskdeck Context Fabric — issue seeds

Last Updated: 2026-08-30

> **As received (2026-08-30 planning pack) — not current scope.** These seeds were superseded the same day
> by the ruled wave on tracker CF-00 `#2254` (children `#2255`–`#2277`) and by ADR-0065; where they differ,
> the issues and the ADR win. Known differences: the ADR is **0065** (not 0063); backfill is
> **ID-preserving**, not "where feasible"; CF-23 ships **only** the SQLite `IBlobStore` (object storage is
> forbidden before ADR-0061 stage 3); and **CF-22 needs a recorded maintainer go on `#2275` in addition to
> the CF-24 evidence** — metrics alone never open the delegated-authority gate.

These are architecture-sized seeds, not one release milestone. Each implementation issue should be split into reviewable PRs.

## CF-00 — Ratify Context Fabric terminology and compatibility boundary

**Goal:** Accept or revise ADR-0063; establish Capture, SourceAsset, Representation, SemanticCandidate, ChangeSet, and AuthorityDecision as the target vocabulary.

**Done when:** decision recorded; ADR-0046 relationship explicit; no runtime changes claimed.

## CF-01 — Introduce durable Capture aggregate and backfill façade

**Scope:**

- `Capture` table with owner, server/client timestamps, lifecycle, producer, intent mode, origin adapter, optional context hint.
- Backfill from capture-shaped `LlmRequest` rows while preserving IDs where feasible.
- Dual-write path for new captures.
- Inbox list/get reads through `ICaptureStore`, not direct queue payload parsing.
- Export/delete/import and migration rollback proof.

**Non-goal:** replacing triage worker in the same PR.

## CF-02 — Split modality, origin, producer, and intent

**Scope:** typed contracts and API compatibility mapping from existing `CaptureSource`; no new overloaded enum values as the canonical model.

**Acceptance:** current clients remain readable; new API exposes the independent dimensions.

## CF-03 — Add ProcessingJob and ProcessingRun

**Scope:** durable job lifecycle, leases, attempts, priority/deadline, cost ceiling, idempotency key, processor/model/config provenance, usage and latency.

**Acceptance:** one existing deterministic extraction path runs through it; capture remains readable during failure.

## CF-04 — Capability registry and processor conformance suite

**Scope:** manifest schema; capability matching; health; installed features/languages/resources; conformance tests.

**First processors:** plaintext, PdfPig, Mock.

## CF-05 — Adapt transcript triage to capability processing

**Scope:** wrap existing transcript worker/extractor behind `audio/text representation → semantic.extract`; remove new-source SQL predicate growth; preserve all golden-path behavior and provenance.

## CF-06 — Representation façade over Transcript and ArtefactExtraction

**Scope:** `IRepresentationStore`; typed read DTOs; no destructive migration; output hashes/schema versions; parent/source lineage.

## CF-07 — General EvidenceAnchor

**Scope:** text spans, time ranges, image/PDF regions, whole source; typed validation; viewer contract; migration from transcript evidence links.

## CF-08 — SemanticCandidate v1

**Kinds:** Action, Decision, Question, Risk, Fact, Reference.

**Scope:** immutable creation, revision/correction, dismissal/supersession, candidate evidence, schema-versioned structured fields.

**Acceptance:** existing transcript extraction emits candidates that compile into byte-for-byte equivalent proposal operations.

## CF-09 — Context resolver and boardless triage

**Signals:** explicit names, current context, integration context, exact aliases, recency, semantic retrieval.

**Acceptance:** capture and understanding never require a board; resolver records reason/confidence; uncertain target stays unresolved or asks one narrow question.

## CF-10 — Processing profiles and routing receipts

**Presets:** Private, Balanced, Controlled, Expert.

**Scope:** egress, allowed processors, quality/latency weights, budgets, residency, language, retention; route decision and alternatives stored.

## CF-11 — Processing cache and selective escalation

**Scope:** cache key by input/processor/model/config/schema; low-confidence escalation; partial reruns; no duplicate API billing.

## CF-12 — Voice-source foundation

**Scope:** audio artefact kinds/MIME validation, duration and waveform metadata, streaming upload, offline queue semantics, retention policy, playback endpoint.

**Non-goal:** transcription provider.

## CF-13 — Lightweight local transcription adapter

**Options spike:** whisper.cpp versus faster-whisper for supported desktop platforms.

**Acceptance:** short single-speaker voice note → transcript representation; CPU/device benchmarks recorded; no diarization.

## CF-14 — WhisperX sidecar adapter

**Scope:** Taskdeck Worker Protocol adapter for transcription, alignment, optional diarization; progress; cancellation; model inventory; structured segment/word output; resource telemetry.

**Acceptance:** existing WhisperX pipeline can be configured without copying its internals into the .NET application.

## CF-15 — One cloud speech adapter and provider benchmark harness

**Scope:** provider-neutral contract; opt-in egress; redacted failures; region/config; cost usage; benchmark corpus.

**Acceptance:** routing can compare local and cloud results on the same fixtures.

## CF-16 — Voice-note UX

**Scope:** record, pause, stop, live provisional transcript, durable failure state, capture intent mode, final receipt, accessibility, hotkey.

## CF-17 — Meeting understanding bundle

**Scope:** speaker mapping, actions/decisions/questions/risks, grouped review, evidence playback, assignment/due conflict checks.

## CF-18 — Local OCR processor

**Scope:** PaddleOCR-class sidecar or selected alternative; language/model inventory; bounding boxes; CPU benchmarks; no domain writes.

## CF-19 — Vision escalation processor

**Scope:** consent-gated multimodal provider; OCR + image-description fusion; image downscaling; egress disclosure; quotas and cost.

## CF-20 — Universal Capture composer

**Scope:** text, paste, voice, image paste/drop, file selection, multi-source capture, optional context, Remember/Organize/Act intent, minimal progress.

## CF-21 — Capture-centered review and activity receipts

**Scope:** group candidates and change sets by capture; progressive disclosure; source evidence viewer; what happened/where it went/undo pointer.

## CF-22 — Authority-policy evaluation v1

**Scope:** one narrow reversible operation class; separate proposer/policy/executor; policy version, expiry, budgets, kill switch, receipt.

**Precondition:** evidence from review acceptance/correction metrics.

## CF-23 — Blob-store abstraction and hosted implementation

**Scope:** SQLite `IBlobStore`; object-store implementation; streaming; content hashes; quotas; export; retention; no change to local ownership guarantee.

## CF-24 — Context Fabric evaluation dashboard

**Metrics:** WER/DER/alignment, OCR accuracy, candidate precision/recall, target accuracy, unchanged acceptance, correction distance, false-action rate, rollback rate, p50/p95 latency, cost per accepted change, local/cloud ratio.

