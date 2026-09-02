# v0.5 bundle — candidate-to-issue map

Last Updated: 2026-09-02

> Curated from `taskdeck-milestone-6-acceleration-bundle-2026-08-30` (grounding FAILED: 0 issues, commit unknown) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Only issue-agnostic material survived; this file supplies the grounding the generator could not. Planning input, not authority.

## What the bundle is and is not

Milestone 6 is **v0.5 — Speak, Type, Paste, or Drop**. The generator captured none of it: the
inventory, issue packs, task queue, dependency analysis and starter map are empty, and
`10_DIAGRAMS/milestone-6-dependencies.dot` is a digraph with no nodes. Nothing in the bundle names a
CF issue, so every mapping below was made here by reading the live issue bodies against ADR-0065 and
`docs/architecture/CONTEXT_FABRIC.md`.

What survived is worth keeping: an architectural spine that independently re-derives the Context
Fabric chain, twelve invariants, and small compile-shaped candidates that encode edge cases the live
issue bodies state as prose. What it is not: authority, a dependency graph, a task queue, or a
second architecture. Where the bundle and the ADR differ, the ADR wins; where the bundle uses its
own vocabulary (`CandidateReviewState`, `AudioSessionState`, `EgressClass`), the shipped enums win.

Precedence, unchanged from `docs/analysis/2026-08-30-acceleration-bundle/RECONCILIATION.md`: live
Git and GitHub → `docs/STATUS.md` and accepted ADRs → this record → the bundle.

Four of the bundle's five schemas (`agent-receipt`, `issue-contract`, `task-claim`, `task-queue`) and
`dependency_planner.py` describe the bundle's own agent process, not Taskdeck's product, and were
not archived. `08_TOOLS/`, `07_AGENT_HANDOFF/` and the empty `01_INVENTORY`, `02_ANALYSIS`,
`04_ISSUE_PACKS` sections are dropped for the same reason.

## Artefact → issue map

Paths are relative to `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.5/`.

| Archived path | Kind | Serves | Verdict | Why |
| --- | --- | --- | --- | --- |
| `candidates/csharp/SemanticCandidate.cs` | C# | CF-08 `#2262` | adapt | Semantic key + payload hash give the rerun-supersession gate a concrete dedupe rule; its state enum and untyped kind must be discarded |
| `candidates/csharp/AudioCaptureSession.cs` | C# | CF-12 `#2266`, CF-16 `#2270` | adopt idea | Chunked upload session with a declared byte ceiling and idempotent chunk re-registration — the missing half of "quota reserved before the stream is read" |
| `candidates/csharp/RetentionPlanner.cs` | C# | CF-12 `#2266` | adopt idea | Dry-run-first retention with pin / accepted-evidence / active-reference holds; the shape CF-12's retention acceptance needs |
| `candidates/csharp/EvidenceValidators.cs` | C# | CF-07 `#2261` (v0.4), consumed by CF-12 / CF-16 | reference only | Duplicates `WorkerProtocol`'s region check; its one new rule (half-open time range bounded by representation duration) is a real gap |
| `candidates/csharp/ProcessingPolicySnapshot.cs` | C# | CF-14 `#2268` via CF-03 `#2257`; CF-10 `#2264` | belongs to v0.6 (profile part); reference for CF-14 | The per-run `Validate(now)` + digest shape is what CF-14's explicit per-run configuration needs; the egress/consent/budget fields are CF-10's |
| `candidates/csharp/ProcessorRouteEvaluator.cs` | C# | CF-10 `#2264` | belongs to v0.6 | Router v1; the v0.6 bundle carries `RouterV1.cs` and `RouteReceipt.cs`. Cost-only ordering conflicts with the profile's ordered preference |
| `candidates/csharp/ConfidenceDecisionGate.cs` | C# | CF-22 `#2275` | belongs to v0.6 | Confidence-as-advisory made executable; unique idea recorded below |
| `candidates/csharp/AutomationSafetyGate.cs` | C# | CF-22 `#2275` | belongs to v0.6 | Shadow/canary ladder; encodes the retired provisional numbers — see *Corrections* |
| `candidates/typescript/captureComposerMachine.ts` | TS | CF-16 `#2270`, CF-20 `#2273` | adapt | The `useVoiceRecording` upload state machine, including the durable failure state CF-16 requires |
| `candidates/typescript/evidenceViewport.ts` | TS | CF-16 `#2270`, CF-21 `#2274` | adopt idea | Normalised-region → pixel mapping for the CF-07 viewer contract |
| `candidates/typescript/presentationProfiles.ts` | TS | CF-21 `#2274` | adapt | Correct Flow/Guided/Control vocabulary and a disclosure-only boundary; missing the shipped-mode migration |
| `candidates/python/score_fixtures.py` | Py | CF-24A `#2319` | adapt | F1 over expected vs actual candidate keys; must be partitioned by candidate kind |
| `candidates/python/canary_report.py` | Py | CF-22 `#2275`, CF-24B `#2277` | belongs to v0.6 | Canary outcome roll-up; its `safeToExpand` rule is not admissible as written |
| `schemas/evaluation-fixture.schema.json` | Schema | CF-24A `#2319` | adapt | id · capability · sourceHash · expectedKeys · **license** — matches the issue's licence-note rule; needs reference-output, method and date fields |
| `fixtures/evaluation-fixtures.example.json`, `fixtures/evaluation-results.example.json` | Fixture | CF-24A `#2319` | adopt idea (shape only) | Two synthetic rows; useful as the file contract, not as corpus |
| `fixtures/canary-events.example.json` | Fixture | CF-22 `#2275` | reject | 50 identical `unchanged` events; makes the expansion rule true by construction |
| `diagrams/multimodal-lineage.mmd` | Diagram | CF-08 `#2262`, CF-12 `#2266`, CF-16 `#2270` | adopt idea | Per-asset lineage: two assets, two representations, two anchor kinds, one candidate set |
| `diagrams/processing-and-route-sequence.mmd` | Diagram | CF-14 `#2268` | adapt | Typed inputs, per-process session secret, spool inputs, inspectable partial state |
| `diagrams/retention-reference-lifecycle.mmd` | Diagram | CF-12 `#2266` | adopt idea | Release-then-delete lifecycle; matches `IBlobStore` reference semantics exactly |
| `diagrams/presentation-profile-boundary.mmd` | Diagram | CF-21 `#2274` | adopt idea | One action set, three disclosures, one audit trail |
| `diagrams/automation-safety-ladder.mmd` | Diagram | CF-22 `#2275` | belongs to v0.6 | Ladder is right in shape; the maintainer's explicit go is missing from it |
| `diagrams/milestone-6-architecture.dot` | Diagram | spine | reference only | Re-derives the ADR-0065 chain minus `ContextBinding` and `AuthorityDecision` |
| `diagrams/agent-integration-train.mmd` | Diagram | none (process) | reference only | Producer-lane / integration-owner pattern; the estate already has worktree doctrine |
| `diagrams/milestone-6-dependencies.dot` | Diagram | none | reject | Empty digraph — the grounding failure made visible |

## Per-issue seeds

### CF-08 `#2262` — SemanticCandidate v1

- **Beyond the issue body.** A deterministic **semantic key** — SHA-256 over
  `owner | run | kind | contractVersion | payloadHash | sorted anchor ids` — turns "a rerun
  supersedes rather than duplicates" from prose into a unique index. Creation refuses a candidate
  with zero anchors, which is the validator rule the issue states only for extractive candidates.
- **Validation against current source.** `SemanticCandidate.cs` (bundle) duplicates
  `backend/src/Taskdeck.Domain/Enums/SemanticCandidateKind.cs` and
  `SemanticCandidateState.cs` with a weaker vocabulary: `CandidateType` is a free string where the
  shipped enum has `Action | Decision | Question | Risk | Fact | Reference`, and
  `CandidateReviewState` (`Pending | Accepted | Rejected | Superseded`) drops `Corrected` and
  `Dismissed`, which #2262 needs for corrections. It carries no `CaptureId`, so it cannot feed the
  `Capture.ActionState` projection. `evidenceAnchorIds` are hashed into the key but never stored —
  `CandidateEvidence` still has to be modelled. The worker-protocol side already exists:
  `ProcessorCandidateBatchOutput` / `ProcessorCandidateOutput` / `ProcessorEvidenceReference` in
  `backend/src/Taskdeck.Application/Processing/Protocol/WorkerProtocol.cs` are what this slice
  persists.
- **Suggested child slices.** The issue already proposes (a)–(d). The bundle sharpens (c): the
  supersession child owns the semantic key, its uniqueness constraint, and a rerun test proving one
  proposal, not two.
- **Startable-now contract work.** None that is independent — CF-08 depends on CF-05 and CF-07. The
  semantic-key definition can be written into the child issue body before admission.

### CF-12 `#2266` — Voice-source foundation

- **Beyond the issue body.** `AudioCaptureSession.cs` models the upload the issue names in one word
  ("streaming upload"): an ordered chunk map, a **declared** byte ceiling checked per chunk, a
  re-registered chunk that is accepted only if its SHA-256 matches (idempotent retry), a finalize
  step that refuses to run with a gap before the final index, and an abort that is illegal after
  finalize. `RetentionPlanner.cs` adds the hold ordering the acceptance criterion implies —
  pinned → accepted evidence → active references → age — and a `DryRunOnly` mode that emits
  `would-delete` decisions, which is how a retention job should be proven before it deletes audio.
- **Validation against current source.** `backend/src/Taskdeck.Application/Interfaces/IBlobStore.cs`
  already has `AcquireAsync` / `AcquireExistingAsync` / `ReleaseAsync` / `OpenReadAsync` /
  `FindByHashAsync` / `GetUsageAsync` with `BlobReference` and `BlobQuotaUsage`, and its own comment
  says wrapping the `byte[]` `ArtefactBlob.Content` "does not make it streaming storage". The chunk
  session is the caller-side complement to that, not a competing store. `RetentionPlanner` must
  release references, never delete by hash (ADR-0065 §Decision 11, amended). `AudioSessionState`
  must not become a domain enum: durable state is
  `CaptureProcessingSummary` + `ProcessingJobState`; the session is endpoint state.
  `RetentionSubject.Kind` as `"source"` string should be the asset's `SourceAssetStorageKind` or
  `CaptureModality`.
- **Suggested child slices.** Not decomposed here; the issue is already single-PR sized apart from
  the upload endpoint, which the chunk session would justify splitting out if it grows.
- **Startable-now contract work.** The audio MIME allow-list with magic bytes, and the audio size-cap
  arithmetic for `docs/platform/CONFIGURATION_REFERENCE.md`, need no CF-23 implementation.

### CF-14 `#2268` — WhisperX sidecar processor

- **Beyond the issue body.** `processing-and-route-sequence.mmd` draws the run contract end to end,
  including two things the issue leaves implicit: the **per-process session secret** and
  **spool-directory inputs** on the host→processor hop, and an inspectable partial state returned to
  the caller while the run continues. `ProcessingPolicySnapshot.cs` offers a shape for the minimal
  per-run configuration CF-03 owes this issue: a contract version, hard limits
  (`MaxInputBytes` / `MaxOutputBytes` / `MaxEstimatedCost` / `Deadline`), a `Validate(now)` that
  rejects an expired deadline, and a digest recorded on the run.
- **Validation against current source.** No `ProcessingPolicySnapshot` type exists in
  `backend/src/`; the name appears only in ADR-0065 §Amendments 8 and `CONTEXT_FABRIC.md` §5 as
  something CF-03 `#2257` defines. The wire side is shipped:
  `ProcessorRunParams` / `ProcessorRunInput` / `ProcessorRunOptions` / `ProcessorRunLimits` and the
  `representation` + `diagnostic` output families in `WorkerProtocol.cs`, plus
  `Processing/Schemas/whisperx-processor.example.json`. Take the validation and digest idea, not the
  record: its egress/consent/allowed-processor fields are CF-10 `#2264` and importing them here would
  re-create the CF-10 dependency the reconciliation removed.
- **Suggested child slices.** Not decomposed here — the issue's three acceptance lines are one
  adapter, one conformance run, one setup document.
- **Startable-now contract work.** The digest must be over a **canonical** encoding; see
  *Corrections*. Recording that rule on CF-03 `#2257` is startable now.

### CF-16 `#2270` — Voice-note capture UX

- **Beyond the issue body.** `captureComposerMachine.ts` is a five-state reducer —
  `idle · capturing · finalizing · submitted · failed{recoverable}` — with transitions guarded by
  the current state, so a late `CHUNK` after finalize cannot resurrect a recording and `FAIL` is
  reachable from anywhere. That is the "durable failure state with retry" acceptance line expressed
  as a testable unit, and it is the natural core of the reusable `useVoiceRecording` the
  reconciliation asks for. `evidenceViewport.ts` gives the evidence viewer the normalised-region →
  pixel conversion with a single guard that rejects non-finite and out-of-unit rectangles.
- **Validation against current source.** `frontend/taskdeck-web/src/composables/useVoiceCapture.ts`
  is **not** a starting point for this: it wraps `SpeechRecognition` (status
  `idle | listening | error`, a `transcript` ref, a webkit-refusal message) and has zero non-test
  importers — exactly the ADR-0033 browser-native path the ADR forbids. CF-16 needs
  MediaRecorder-to-stored-audio, so the composable is the deletion half of its third acceptance
  line, and the bundle's machine is a better base. `evidenceViewport.ts` overlaps
  `frontend/taskdeck-web/src/components/review/TranscriptEvidenceViewer.vue`, which is char-span
  only; region and time-range viewing are new surface.
- **Suggested child slices.** Not decomposed here.
- **Startable-now contract work.** None — CF-16 depends on CF-12 and CF-13/CF-14.

### CF-20 `#2273` — Universal Capture composer

- **Beyond the issue body.** Only one thing, and it is a constraint rather than a feature: the
  composer state machine belongs to **one** unit that both hosts mount. The bundle's reducer is that
  unit; CF-20's child (d) should import it from CF-16, not restate it, or the "no board required"
  and "recording never lost" guarantees fork between two composers.
- **Validation against current source.** `frontend/taskdeck-web/src/views/paper/inbox/PaperCaptureComposer.vue`
  is the surface being replaced; the reducer has no dependency on it. The bundle adds nothing for
  multi-source captures, offline binary queueing, or the 40-screenshot degradation case.
- **Suggested child slices.** Not decomposed here — the issue's (a)–(f) split stands.
- **Startable-now contract work.** None.

### CF-21 `#2274` — Capture-centred review and presentation profiles

- **Beyond the issue body.** `presentationProfiles.ts` makes the boundary executable: one function
  maps `'flow' | 'guided' | 'control'` to three disclosure booleans and passes capability
  availability through untouched, with a comment stating that commands and permissions are identical
  across profiles. `presentation-profile-boundary.mmd` says the same thing as a picture — three
  profiles, one command set, one audit trail. Together they are the regression fence for "profiles
  do not fork domain semantics".
- **Validation against current source.** `frontend/taskdeck-web/src/types/workspace.ts` still
  declares `workspaceModes = ['guided', 'workbench', 'agent']`;
  `backend/src/Taskdeck.Domain/Enums/WorkspaceMode.cs` still has `Guided | Workbench | Agent` with
  `WorkspaceModeContract` string values. Both are what ruling 9 replaces. The bundle's type omits
  the migration the issue requires: `guided→Guided`, `workbench→Control`, `agent→Control`, and
  retiring the *Agent* selector retires nothing else. Vocabulary is correct — *Control*, never
  *Controlled*.
- **Suggested child slices.** Not decomposed here.
- **Startable-now contract work.** The disclosure matrix (which profile shows candidates, operation
  JSON, route receipts, reruns) can be written and unit-tested before CF-08/CF-09 data exists.

### CF-24A `#2319` — Benchmark corpus and command

- **Beyond the issue body.** `evaluation-fixture.schema.json` is a usable first draft of the
  per-fixture record, and it already carries the `license` field the issue makes mandatory plus a
  `sourceHash` pinned to 64 hex characters, which is how a fixture is proven unmodified.
  `score_fixtures.py` is a small deterministic scorer (set F1, sorted iteration, mean over fixtures)
  that writes a JSON report — the shape the reproducible benchmark command should print.
- **Validation against current source.** No benchmark corpus, fixture schema or benchmark command
  exists in the repository today. The schema is missing the reference-output pointer, the measuring
  **method** and **date** the issue requires beside each fixture, and a marker for the hostile-
  injection variants (`#1323`). `score_fixtures.py` scores one flat key set, but the issue asks for
  candidate precision/recall **by kind** — it needs partitioning over `SemanticCandidateKind` before
  it means anything.
- **Suggested child slices.** Not decomposed here; the corpus and the command are already the two
  natural halves and the issue is ordered before CF-13.
- **Startable-now contract work.** The fixture record schema, the licence/method/date convention and
  the report format are all definable before any processor exists — this is the most startable v0.5
  contract in the bundle.

## Invariants cross-check

| # | Bundle invariant | Counterpart | Status |
| --- | --- | --- | --- |
| 1 | Raw source ownership is explicit and immutable | `CONTEXT_FABRIC.md` §6 rule 3; ADR-0065 §Decision 1 | already stated |
| 2 | Capture disposition is independent from processor success | §6 rules 1 and 4; ADR §Decision 1 (three axes) | already stated — Taskdeck is stronger (`Partial`, projections) |
| 3 | Job and run records are the truth for processing state and provenance | ADR §Decision 6; §2 (the queue row is the job record until CF-03) | already stated |
| 4 | Processor outputs are immutable, typed and hashable | ADR §Decision 3 | already stated — Taskdeck adds quality state and forward supersession |
| 5 | Evidence always points to a representation or source through a typed anchor | ADR §Decision 4; §1 chain | already stated |
| 6 | Semantic candidates are persisted before promotion into proposals | ADR §Decision 5, ruling 3 | already stated |
| 7 | Route decisions snapshot policy, cost and egress constraints | ADR §Decision 6; §6 rule 7 | stronger here — adds a policy **digest** on the receipt |
| 8 | Remote processing requires explicit policy/consent evidence | ADR §Decision 6, ruling 5 | already stated |
| 9 | Confidence is advisory; authorization and operation risk remain hard gates | ADR §Decision 9 | stronger here — a risk class is excluded from automation at any confidence |
| 10 | Automated mutations require shadow, canary, compensation and kill-switch proof | ADR §Amendments 10; CF-22 `#2275` | **conflicts** — omits the maintainer's explicit go, which no gate may be automated past |
| 11 | Presentation profiles do not fork domain semantics | ADR §Three independent policy families | already stated — the bundle adds an executable boundary |
| 12 | Every first vertical has a deterministic fixture and replay contract | ADR §Decision 8; CF-24A `#2319` | **new** as phrased — the ADR requires the corpus before adaptivity; per-vertical replay is a stronger, useful reading |

## Corrections to the bundle

| Claim or artefact | Correction |
| --- | --- |
| Milestone 6 has 0 issues, grounding commit `unknown` | Milestone 6 is **v0.5 — Speak, Type, Paste, or Drop**: CF-00 `#2254` (tracker), CF-08 `#2262`, CF-09 `#2263`, CF-12 `#2266`, CF-13 `#2267`, CF-14 `#2268`, CF-16 `#2270`, CF-20 `#2273`, CF-21 `#2274`, CF-24A `#2319`, GEN-03 `#1317`, GEN-06 `#1320`, plus code-health `#2352` and `#1607`. Grounding here is `main` `79dd57cd3` |
| `AutomationSafetyGate` / `canary_report.py` treat ≥50 events, zero reversals and ≤10% edits as `safeToExpand` | Those numbers were **struck through** on `#2275` and in ADR-0065 §Amendments 10 as provisional orientation only. The real gate is risk-based **plus the maintainer's explicit go**. No script may compute expansion eligibility |
| `CandidateReviewState` (`Pending / Accepted / Rejected / Superseded`) | Conflicts with shipped `SemanticCandidateState` (`Proposed / Corrected / Accepted / Dismissed / Superseded`). Do not introduce; `Corrected` is required by CF-08 |
| `AudioSessionState`, `EgressClass`, `CandidateRiskClass` as new enums | Do not add. Durable capture state is the three axes plus `ProcessingJobState`; egress class is CF-10's profile field |
| `ProcessingPolicySnapshot.ComputeDigest` serialises the record with default options | Not canonical: property order follows declaration, `decimal` and `DateTimeOffset` formatting are unpinned, and a later field reorder silently changes every stored digest. A persisted route receipt needs a canonical encoding with a pinned field order |
| `ProcessorRouteEvaluator` orders eligible processors by cost, then id | Router v1 is hard constraints followed by the **profile's ordered preference** (ADR §Decision 6; §6 rule 7). Cost ordering is a scoring rule and is forbidden before the CF-24A corpus exists |
| `EvidenceValidators` presented as new validation | `WorkerProtocol.cs` already validates normalised rectangles (with a tolerance) and confidence ranges. Its **new** contribution is real though: the protocol currently accepts `EndMs == StartMs` and never bounds a time range by the representation's duration |
| `SemanticCandidate.Create` takes `evidenceAnchorIds` | They are hashed into the semantic key and then discarded. CF-08 needs `CandidateEvidence` rows with field name and derivation |
| `RetentionPlanner` returns `Delete` decisions | Deletion is a **reference release** through `IBlobStore.ReleaseAsync`; bytes go only when the last reference does. Never delete by hash |
| `score_fixtures.py` computes one flat F1 | CF-24A wants precision/recall **by candidate kind**; partition over `SemanticCandidateKind` |
| `canary-events.example.json` — 50 `unchanged` events | Synthetic and uniform; it makes the expansion rule true by construction and is not evidence of anything |
| Bundle spine `Capture → … → Proposal → Shadow/canary` | Omits `ContextBinding` (CF-09) and `AuthorityDecision` / `Execution` / `Receipt`. The chain is ADR-0065 §Decision, `CONTEXT_FABRIC.md` §1 |
| `presentationProfiles.ts` names three profiles | Correct, but silent on the migration ruling 9 requires: `guided→Guided`, `workbench→Control`, `agent→Control`, and the *Agent* selector's retirement removes nothing else |
| `milestone-6-dependencies.dot` | Empty digraph. Derive dependencies from live issue bodies, never from this file |

## NOT verified

- This map was written from source reading only. The bundle's self-checks were reproduced by the
  coordinator on this box on 2026-09-02, separately from this map (receipt in `../RECONCILIATION.md`):
  8 Python tests passed, the 3 TypeScript files pass `tsc --strict --noEmit`, and the 8 C# files build
  together in an isolated `net8.0` class library with 0 warnings. None of that proves repository
  integration; no candidate was compiled inside the Taskdeck solution.
- No GitHub call was made. Issue state comes from cached bodies captured for this pass; live
  labels, comments, milestone membership and project status were not re-read.
- Delivery state was verified only for CF-01 `#2255` (closed, in force per `docs/STATUS.md`). The
  v0.4 predecessors CF-03 `#2257`, CF-04 `#2258`, CF-06 `#2260`, CF-07 `#2261` and `#1429` were read
  as issue bodies, not measured against `main`.
- The `.svg` renders under `diagrams/` were not opened; they are assumed faithful to their `.mmd` /
  `.dot` sources.
- No corpus sizing, licence review, or byte-budget arithmetic was done for CF-24A `#2319`.
- Whether `AudioCaptureSession`'s chunk model fits the eventual CF-23 streaming implementation is
  unproven — `IBlobStore` has no implementation registered.
