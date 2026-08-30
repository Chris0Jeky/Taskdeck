# External audit of PR #2280 — as received (2026-08-30)

Last Updated: 2026-08-30

**Provenance.** The maintainer asked an external LLM to audit what the Context Fabric pass (PR
`#2280`, ADR-0065) had done and handed the result to the agent pass with the instruction to
implement it. This file is that audit **verbatim, as received** — it is evidence, not repository
doctrine. Its disposition (what was accepted, amended, or declined, and where each item landed) is
`AUDIT_RECONCILIATION.md` beside it; the decision record is ADR-0065's *Amendments* section.

---

# Overall assessment

I audited PR **#2280**, the merged architecture and strategy documents, the new milestone ladder, the CF issue wave, the implementation scaffold, review residuals, and the exact-head CI evidence.

**My judgment: keep PR #2280 merged.** It is a credible architecture pivot and a safely staged scaffold. It is not merely a mass of speculative issues. The agent understood the intended thesis, reconciled it against Taskdeck's existing model, preserved compatibility, and implemented enough structure to make the direction concrete.

However, I would **not start executing CF-01 onward mechanically yet**. There should first be one small architecture-reconciliation pass. Several contracts were frozen one step too early, the dependency graph contains real inconsistencies, and the architecture is currently missing the actual entity immediately after `Capture`: the general `SourceAsset`.

| Area                                   |                                 Assessment |
| -------------------------------------- | -----------------------------------------: |
| Product direction                      |                            **Very strong** |
| Reuse of existing Taskdeck foundations |                            **Very strong** |
| Migration safety                       |                                 **Strong** |
| Issue seeding and traceability         | **Strong, but some issues are epic-sized** |
| Data-model completeness                |       **Incomplete in one critical place** |
| Worker protocol                        |   **Promising draft, not yet a stable v1** |
| Milestone sequencing                   |                       **Needs correction** |
| Immediate regression risk              |                                    **Low** |

PR #2280 changed 70 files, added 8,929 lines, introduced a real migration and executable contracts, and merged at `fd09dd96a`. The exact PR head passed the full required CI matrix, including backend, frontend, API integration, migration validation, architecture tests, security scans, containers, docs governance, and E2E smoke. I did not independently clone and rerun the suite locally, but the GitHub evidence is strong and specific.

The runtime scaffold is also appropriately conservative: `ContextFabric:DualWriteCaptures` defaults to off, the existing queue remains authoritative, and the migration creates an empty `Captures` table rather than attempting a live backfill during the planning PR. There is no reason to revert it.

---

# What the agent did particularly well

## 1. It did not blindly transpose the bundle

The agent compared the proposal with the live repository and adapted it:

* ADR-0065 reflects the current `LlmRequest`-backed capture architecture.
* It recognizes that `CaptureSource` combines modality, origin, transport, and producer.
* It reuses the shipped transcript, artefact, provenance, proposal, review, and executor machinery.
* It deferred utility-scored routing until Taskdeck has an evaluation corpus.
* It kept the modular monolith and treated sidecars as controlled processor boundaries rather than turning Taskdeck into speculative microservices.
* It explicitly marked provider prices as unverified rather than treating the pack as ground truth.

That is exactly the right mode of operation for this kind of architectural handoff.

## 2. The central architectural decision is correct

Promoting `Capture` from a queue-shaped JSON payload into a durable user-owned aggregate is the right move.

The most important invariant is sound:

> A capture remains valid and readable even when transcription, OCR, semantic extraction, routing, or planning fails.

That separates durable user information from retryable computational machinery. It directly addresses the current fusion of capture state and job state inside `LlmRequest`. The new `Capture` entity is ID-preserving, explicitly transitional, and does not prematurely replace the current Inbox read path.

## 3. The compatibility strategy is strong

Using:

```text
Capture.Id = legacy LlmRequest.Id
```

is the correct backfill strategy. It preserves existing `CreatedFromCaptureId`, capture provenance references, transcript references, and historical identities without creating an expensive cross-reference migration.

The default-off dual-write flag is also correct. It provides a controlled sequence:

```text
empty table
→ backfill
→ dual-write
→ parity verification
→ read switch
→ retire legacy ownership
```

That is substantially safer than attempting one large schema and worker rewrite.

## 4. Capability-based processing is the right destination

Moving from:

```text
CaptureSource / request-prefix / worker-specific SQL
```

to:

```text
capability + processor registry + policy
```

is the right abstraction. The capability vocabulary is sensibly focused around transformations such as transcription, OCR, normalization, semantic extraction, context resolution, and change planning.

The decision to reuse the same proposal/review/execution machinery is especially important. The agent did not create:

* a second task representation;
* a direct multimodal-to-card path;
* a special "voice task" domain;
* a privileged sidecar mutation API.

That preserves Taskdeck's main differentiated asset.

## 5. The roadmap language is disciplined

The milestone ladder distinguishes:

* shipped facts;
* committed direction;
* leaning themes;
* provisional themes.

That avoids pretending v0.7–v1.0 are fully designed releases. The public line is also substantially better than "anything enters":

> Speak, type, paste, or drop. Taskdeck turns context into accountable work, under your rules.

It names the high-value user actions without claiming universal ingestion.

## 6. Human authority was not silently erased

The agent preserved a separate human gate for CF-22 and made the delegated ADR rulings explicitly revisable. That is a defensible interpretation of your instruction to go as far ahead as possible.

It also added only **two new Context Fabric human items** to `OUTSTANDING_TASKS.md`:

1. confirm or overturn the nine delegated rulings;
2. explicitly authorize CF-22 after evidence exists.

The handoff's phrase "§I — 35 open" is misleading. Thirty-five may be the total number of unchecked items across the entire long-standing file, but Context Fabric section I contains two unchecked human actions, not 35 new decisions.

---

# What must be corrected before implementation advances

## 1. The architecture is missing a general `SourceAsset`

This is the largest gap.

The architecture says:

```text
Capture
  → SourceAsset
  → Representation
```

But the scaffold currently implements:

```text
Capture
  → [nothing general]
  → future Representation façade
```

The new `Capture` entity has metadata such as title, modality, intent, producer, and context hint, but it has no asset collection and no canonical raw-content field. Its `UserNote` is capped at 2,000 characters, whereas ordinary capture text currently supports much larger input.

Existing `SourceArtefact` can serve as a compatibility payload for files and binary assets. It does not solve:

* typed text;
* pasted text;
* a URL plus a user instruction;
* text plus a screenshot in one capture;
* voice plus a typed clarification;
* structured integration events;
* a capture containing several immutable inputs.

CF-12 talks about adding an audio source asset, and CF-20 assumes multiple source assets, but there is no foundational issue that defines the general source model they depend on.

This creates a concrete ownership hole in CF-01: it says `LlmRequest.Payload` will stop being mutable capture state, but it does not identify where the immutable raw text will live afterwards. Unless corrected, the queue row will remain the de facto source store, undermining the separation the migration is meant to establish.

### Required correction

Add a foundational slice before or inside CF-01:

```text
SourceAsset
- Id
- CaptureId
- Kind / Modality
- MediaType
- ContentHash
- ByteSize
- StorageKind
- BlobObjectId?
- TextPayloadId?
- ExternalReference?
- OriginalName?
- CreatedAt
```

Typed text should become an immutable source asset, not a field on the processing job. `SourceArtefact` should initially be adapted behind this model rather than destructively replaced.

**This is a hard prerequisite for a correct CF-01 implementation.**

---

## 2. `CaptureLifecycleState` combines three different state machines

The current enum contains:

```text
Received
Preparing
Understood
Routed
NeedsReview
Acted
Kept
Failed
Archived
```

That looks simple in a UI timeline, but it combines:

* processing state: `Preparing`, `Understood`;
* planning/action state: `Routed`, `NeedsReview`, `Acted`;
* user disposition: `Kept`, `Archived`;
* failure summary: `Failed`.

This will fail once captures have multiple assets and multiple jobs.

A capture can simultaneously be:

* kept by the user;
* fully transcribed;
* partially OCR-processed;
* waiting for one failed processor retry;
* understood;
* not yet targeted;
* linked to an already applied change;
* later archived.

One mutable enum cannot represent those combinations without losing information. For example, moving `Acted → Archived` removes `Acted` as the current state even though the action outcome remains true. More seriously, one failed image processor should not put a text-plus-image capture into one global `Failed` state when the text path succeeded.

### Recommended model

```text
CaptureDisposition
- Active
- Kept
- Archived

ProcessingSummary     // preferably derived
- Idle
- Processing
- Partial
- Ready
- Failed

ActionState           // derived from candidates/change sets
- Unplanned
- NeedsInput
- NeedsReview
- Acted
```

The UI can still display one understandable timeline:

```text
Received → Preparing → Understood → Needs review → Acted
```

But that timeline should be a projection over authoritative records, not the only persisted truth.

At minimum, add explicit `Partial` and `NeedsInput` semantics before multi-source processing begins.

---

## 3. The Worker Protocol is a good prototype but not a coherent v1 contract

The current protocol is internally inconsistent with its own capability vocabulary.

The capability list permits:

```text
semantic.extract
context.resolve
change.plan
change.verify
```

But `ProcessorRunResult` can only return `Representations`, and successful runs are required to emit at least one representation. Every representation also has a mandatory text payload.

That means the protocol cannot naturally return:

* semantic candidates;
* context bindings;
* change plans;
* verification findings;
* image-region-only outputs;
* document structure;
* structured event data;
* binary audio preprocessing output.

There are other structural limitations:

* `ProcessorRunInput` accepts one asset only.
* It cannot accept several source assets or representations.
* Options are mostly speech-specific.
* `outputSchemas` are declared globally rather than per capability.
* usage cannot represent tokens, pages, bytes processed, billable units, or calculated cost.
* segments support character/time information but not image or page geometry.
* the protocol already has known validator/schema inconsistencies recorded against CF-04.

### Strongest correction

Treat the current contract as:

> **Taskdeck Worker Protocol v1-alpha**

Do not call it fixed until at least two materially different processors pass conformance:

1. PdfPig extraction through the memory-contained worker;
2. WhisperX through the sidecar path.

For the first stable protocol, I would keep sidecars narrow:

```text
Externalizable in v1:
- content.inspect
- text.normalize
- document.extract-text
- image.ocr
- image.describe
- audio.preprocess
- audio.transcribe
- audio.align
- audio.diarize
- possibly semantic.extract with a typed CandidateBatch result

Keep in-process initially:
- context.resolve
- change.plan
- change.verify
- authority evaluation
- execution
```

Those latter capabilities require current domain state, permissions, policy, and concurrency semantics. They should not casually become remote processor contracts.

The request should accept typed multiple inputs:

```text
inputs:
- SourceAssetRef
- RepresentationRef
- bounded ContextSnapshotRef
```

The result should be a discriminated union:

```text
RepresentationOutput
CandidateBatchOutput
DiagnosticOutput
```

Representation payloads should also be typed rather than always requiring `Text`.

---

## 4. `IBlobStore` needs reference semantics, not delete-by-hash semantics

The current contract stores blobs by owner and content hash and proposes same-owner deduplication, but deletion is:

```csharp
DeleteAsync(ownerUserId, contentHash)
```

Suppose one user uploads the same screenshot twice and two `SourceAsset` records reference the same deduplicated blob. Deleting either asset cannot safely call delete-by-hash without knowing whether another asset still references the bytes.

The contract needs one of:

```text
BlobObject + BlobReference + reference count
```

or:

```text
AcquireBlob(...)
ReleaseBlob(referenceId)
```

with deletion occurring transactionally only after the last reference disappears.

Other corrections:

* MIME type belongs primarily to the source asset/reference; identical bytes may arrive with different declared media types.
* `PutAsync` should receive expected size and asset kind so quota can be reserved before consuming an unbounded stream.
* stream ownership and disposal should be explicit.
* per-kind quota should be part of the contract or surrounding service, not reconstructed after storage.
* the existing `ArtefactBlob.Content` is a `byte[]`; putting it behind a streaming interface does not automatically create true streaming storage. A large-audio implementation will need SQLite incremental BLOB I/O, bounded chunk rows, or a controlled spool-to-database strategy.

Confirm SQLite as the local implementation. Amend the interface before CF-23 implements it.

---

## 5. The capture dimensions need more precise semantics

The separation into modality, origin, producer, and intent is directionally right. Some current definitions need refinement.

### Producer identity

`CaptureProducerKind` currently includes:

```text
Human
Agent
Integration
Import
```

`Import` is usually a transport or origin, not a principal kind. A human can initiate an import; an integration can perform an import.

More importantly, the model records the producer kind but not the producer principal. `UserId` is the owner, not necessarily the agent/service/human that produced the capture.

Use:

```text
OwnerPrincipalId
ProducedByPrincipalId?
ProducerKind
```

This will matter for teams, MCP, service accounts, integrations, and audit.

### Requested versus effective intent

`Auto` currently sits alongside `Remember`, `Organize`, and `Act`.

But `Auto` is not an effective intent. It is an instruction to infer one.

Use:

```text
RequestedIntent = Auto
EffectiveIntent = Organize
IntentDerivedByRunId = ...
```

Otherwise downstream code repeatedly needs to ask whether `Intent` is a real result or unresolved policy.

### Primary modality

`PrimaryModality` should be treated as a summary or UI hint once multi-source captures exist. Routing must operate on each `SourceAsset`, as ADR-0065 already intends.

### Legacy source

`LegacySource` is persisted, so it is not strictly a derived compatibility field. It is a compatibility snapshot. Either rename it accordingly or derive it for native captures at the API boundary.

---

## 6. `IRepresentationStore` was declared "fixed" before its invariants were settled

The current `RepresentationDescriptor` has a non-nullable `CaptureId`, even though shipped transcripts and artefact extractions can exist without a corresponding durable Capture. This exact incompatibility is already recorded on CF-06.

It also omits `QualityState`, despite both the ADR and CF-06 issue describing quality state as part of the representation header.

Before implementation, settle:

* whether legacy orphan representations receive synthetic/backfilled captures;
* whether `CaptureId` may temporarily be null;
* parent representation/source invariants;
* representation supersession semantics;
* quality state;
* typed payload ownership;
* whether the façade is read-only permanently or only during migration.

The comments describing the contract as "fixed" should be softened until this is resolved.

My preference is to **backfill a durable Capture for every retained legacy source** where ownership can be established. A null `CaptureId` is useful during a migration window but should not be the permanent target model.

---

## 7. An alternate capture creation route currently bypasses dual-write

`CaptureService.CreateAsync` contains the new dual-write seam. `LlmQueueService.AddToQueueAsync` still accepts capture-shaped request types and persists only an `LlmRequest`.

This has no current runtime impact because the feature flag is off. It becomes a correctness defect the moment dual-write or Capture-backed Inbox reads are enabled.

Before CF-01 switches anything on, all capture creation must pass through one canonical intake service. Preferably:

```text
CaptureIntakeService
    creates Capture
    stores SourceAssets
    creates processing jobs when requested
```

Legacy queue endpoints can call that service through adapters.

---

# The issue and milestone graph needs correction

## The published "first vertical" is not topologically valid

The tracker currently states:

```text
CF-01
→ CF-12
→ CF-14
→ CF-06/07
→ existing triage
→ CF-16
```

But:

* CF-12 depends on CF-01, CF-02, and CF-23.
* CF-14 depends on CF-04, CF-06, CF-07, and CF-12.
* CF-04 depends on CF-03.
* CF-05 is the planned adaptation that takes representations into `semantic.extract`.
* CF-16 depends on CF-07 and one transcription implementation.

The tracker's own dependency table contradicts its headline sequence.

A valid order is:

```text
Preflight architecture reconciliation
    ↓
CF-01 Durable Capture + general SourceAsset foundation
    ↓
┌───────────────┬────────────────┬──────────────┐
│ CF-02         │ CF-03          │ CF-23        │
│ dimensions    │ jobs/runs      │ blob store   │
└───────────────┴────────────────┴──────────────┘
    ↓
┌───────────────┬────────────────┬──────────────┐
│ CF-04         │ CF-06          │ CF-12        │
│ worker host   │ representations│ audio source │
└───────────────┴────────────────┴──────────────┘
    ↓
┌────────────────────────┬─────────────────────┐
│ CF-05 semantic.extract │ CF-07 anchors       │
└────────────────────────┴─────────────────────┘
    ↓
CF-14 WhisperX dogfood route
and/or CF-13 lightweight user route
    ↓
CF-16 voice UX + audio evidence
    ↓
existing proposal/review/apply
```

## CF-24 is scheduled after work that depends on its fixtures

CF-13 says its benchmark uses fixtures seeded by CF-24, but CF-13 is v0.5 and CF-24 is v0.6.

Split CF-24:

```text
CF-24A — committed corpus + benchmark command
v0.4/v0.5, before CF-13

CF-24B — runtime outcome metrics + dashboard
v0.6, after candidates and processing runs
```

## CF-14 depends conceptually on processing profiles that arrive later

CF-14 says diarization and alignment are driven by the processing profile. Full profiles and routing are CF-10 in v0.6.

For v0.5, either:

* pull a minimal `ProcessingPolicySnapshot` into CF-03/CF-14; or
* make CF-14 use explicit per-run configuration and let CF-10 take control later.

Do not create a hidden dependency on a later release.

## CF-20 risks rebuilding CF-16

CF-16 adds voice recording to the Paper composer. CF-20 later replaces the capture surfaces with Universal Capture and depends on CF-16.

To avoid building the UI twice, CF-16 should produce a reusable:

```text
VoiceCaptureController / useVoiceRecording
VoiceCapturePanel
upload/retry state machine
```

Universal Capture should host that component. It should not absorb code written directly into a soon-to-be-retired composer.

## CF-22 should not block v0.6

CF-22 depends on evidence produced by the same general release band and then requires your explicit authorization. Its inclusion in the v0.6 milestone is acceptable as a stretch target, but it must not become a release blocker.

Keep it:

```text
milestone = v0.6
status = Blocked / Stretch
release-blocker = false
```

until the evidence and human ruling exist.

---

# Milestone assessment

The broad ladder is useful. I would retain v0.5–v1.0 as horizons.

The problem is **v0.4**, which currently contains four major theses:

1. hosted public beta;
2. canonical work-model expansion;
3. Context Fabric foundation and worker containment;
4. refactoring and performance work.

That is a large amount of schema, infrastructure, security, product, and deployment change for one solo-maintainer release.

You do not necessarily need to renumber it again. Use explicit internal release gates:

```text
Gate A — Fabric persistence
Capture, SourceAsset, jobs/runs, representations, anchors, blob semantics

Gate B — Processor containment
Worker protocol proven with PdfPig; security and resource caps

Gate C — Trusted hosted instance
Backup/restore, key custody, private users, operating runbook

Gate D — Public hosted beta
Untrusted-user threat model, registration, cost ceilings, incident path
```

Public registration must be the final v0.4 gate, not something developed in parallel and opened while foundational migrations are still moving.

For v0.5, the release gate should require **one genuinely accessible speech route for an ordinary user**. WhisperX through a manually configured Python/CUDA environment is a good dogfooding route, but it is not sufficient for the public "Speak" promise. The lightweight CF-13 route must become one-click, downloaded-on-enable, or clearly bundled; alternatively, a consented managed route must be available.

---

# My recommendation on the nine delegated rulings

The rulings are mostly good. I would not overturn the architecture.

| # | Ruling                                                    | Recommendation                                                          |
| - | --------------------------------------------------------- | ----------------------------------------------------------------------- |
| 1 | Context Fabric internal name and public wedge line        | **Confirm**                                                             |
| 2 | v0.4 foundation, v0.5 payoff                              | **Confirm with explicit v0.4 gates and release-blocker classification** |
| 3 | Persist `SemanticCandidate`                               | **Confirm**                                                             |
| 4 | `IBlobStore` with SQLite locally                          | **Confirm principle; amend the interface and reference model**          |
| 5 | Balanced processing default with first-use remote consent | **Confirm**                                                             |
| 6 | First delegated class and 50/90%/zero-reversal gate       | **Amend the evidence gate**                                             |
| 7 | ID-preserving Capture backfill                            | **Confirm**                                                             |
| 8 | Amend existing ADRs instead of replacing history          | **Confirm**                                                             |
| 9 | Flow/Guided/Control replace workspace modes               | **Confirm with one caveat**                                             |

## Ruling 6 needs a better evidence gate

Fifty observations with zero reversals do not demonstrate an extremely low failure rate. Under the basic "rule of three," zero failures in 50 observations still leaves an approximate 95% upper bound near 6%.

Use evidence dimensions rather than one magical threshold:

* target board accuracy;
* permission and ownership accuracy;
* unchanged-acceptance rate;
* false-action rate;
* correct empty/no-action rate;
* compensation/undo reliability;
* zero cross-user or cross-board violations;
* shadow-policy results before any real auto-execution;
* initial daily operation ceiling;
* immediate kill switch;
* your explicit go.

The `≥50`, `≥90%`, and zero-reversal figures can remain provisional orientation numbers, not automatic authorization.

## Ruling 9 caveat

Retire **Agent as a workspace-mode selector**, because it is currently byte-identical to Workbench and therefore misleading. Do not retire Agents, Runs, agent attribution, or agent capabilities from Taskdeck.

This does reverse the earlier "keep Agent mode for now" ruling, but it does so for a coherent reason: agent behavior belongs inside project context and accountable automation, not inside a fake presentation mode. The existing issue already proves that the selector currently changes nothing.

Also keep the three policy vocabularies visibly separate:

```text
Presentation: Flow / Guided / Control
Processing:   Private / Balanced / … / Custom
Authority:    Observe / Suggest / Assist / Operate / …
```

`Control` and `Controlled` are too similar. Rename the processing preset before it becomes UI copy.

---

# Smaller cleanup findings

These are not architectural blockers, but should be corrected in the reconciliation pass.

* The `context-fabric` label description references **ADR-0064**, while the actual decision is ADR-0065.
* CF-01 has an early review comment claiming account deletion does not delete durable Captures, but the merged code now explicitly does. Annotate that comment as resolved so future agents do not duplicate the work.
* All CF-04 protocol residuals should be promoted into its acceptance checklist, not left only in comments. They include enum validation, schema/validator parity, null warning handling, notification-envelope validation, cancellation, session secrets, and example-file drift.
* CF-04, CF-08, and CF-20 are effectively epics. Keep their issue numbers as umbrella slices, but split them into one-PR children before moving them to `Now`.
* "Accepted under delegation" is honest but not an ideal permanent ADR status. After your confirmation comment, ADR-0065 should simply become **Accepted**, with the amendments and confirmation date recorded.

---

# Recommended comment for CF-00 #2254

```markdown
I confirm ADR-0065 and the Context Fabric direction, with the amendments below.

Confirmed:
1. Context Fabric as the internal architecture name and the public "Speak, type, paste, or drop" wedge line.
3. Persisted SemanticCandidate.
5. Balanced processing by default, with explicit first-use consent before remote processing.
7. ID-preserving Capture backfill.
8. Amend existing ADRs in place rather than replacing their history.
9. Flow / Guided / Control as presentation profiles. Retiring the Agent selector does not remove Agents, Runs, agent attribution, or agent capabilities from Taskdeck.

Confirmed with amendments:
2. v0.4 retains the Fabric foundation and hosted-beta direction, but uses explicit gates:
   A. Capture/source/job migration
   B. worker containment
   C. trusted hosted instance and restore/security proof
   D. public registration
   Milestone membership alone does not make every child a release blocker.

4. IBlobStore with SQLite locally is confirmed, but CF-23 must first define blob identity/reference or transactional reference-count semantics, streaming quota reservation, and media-type ownership on the asset/reference.

6. The 50 proposals / 90% unchanged / zero reversals figures remain provisional. CF-22 stays blocked on a risk-based shadow and canary report, target and permission accuracy, compensation proof, kill-switch proof, and my explicit authorization.

Before CF-01 or CF-04 hardens the contracts, reconcile:
- a general SourceAsset/CaptureAsset foundation, including immutable typed and pasted text;
- orthogonal capture disposition, processing summary, and action state rather than one lifecycle enum;
- producer principal identity and requested versus effective intent;
- Worker Protocol v1 as a draft until PdfPig and WhisperX both pass conformance, with multiple typed inputs and typed output families;
- IBlobStore reference semantics;
- IRepresentationStore legacy backfill and quality-state semantics;
- the first-vertical dependency order;
- CF-24A fixtures before CF-13 and CF-24B metrics later.

With those amendments, ADR-0065 is confirmed as Accepted.
```

# Bottom line

The agent made the **right architectural move** and handled the delegation responsibly. PR #2280 is worth keeping.

The largest mistake is not bad implementation; it is that the plan jumps directly from `Capture` to `Representation` without first making **SourceAsset** real. The second-largest is treating the first Worker Protocol draft as though it already supports the full capability model.

Resolve those two items, split the overloaded capture lifecycle, repair the blob contract and dependency graph, and the Context Fabric becomes a genuinely strong foundation rather than a sophisticated roadmap whose first few implementations create new compatibility debt.
