# Context Fabric — architecture reference

Last Updated: 2026-08-30

**Authority:** ADR-0065 (`docs/decisions/ADR-0065-context-fabric-capture-representation-processing.md`),
Accepted — confirmed by the maintainer on 2026-08-30 with the amendments recorded in its *Amendments*
section (tracker CF-00 `#2254`). This page is the agent-facing map of the architecture: what is
shipped, what the scaffold PR (`#2280`) and the same-day reconciliation pass add, what each CF issue
adds, and where the seams are. Shipped truth stays in `docs/STATUS.md`. The external audit that drove
the reconciliation and its disposition: `docs/analysis/2026-08-30-context-fabric/AUDIT_RECONCILIATION.md`.

**Status (2026-08-30, after CF-01 `#2255`).** The durable `Capture` aggregate and its `SourceAsset`s
are live, not scaffolding: `ContextFabric:DualWriteCaptures` defaults **on**, an ID-preserving
startup backfill brings every pre-existing capture-shaped `LlmRequest` row into the aggregate under
its own id, and Inbox list / get resolve a capture's own material through `ICaptureStore` rather
than parsing the queue payload. The read switch is armed by the `CaptureBackfillStates` marker and
degrades per item, so a capture without a durable row is still read from its queue row. The queue
row remains the **job** record until CF-03 replaces it.

## 1. The invariant

```text
Capture → SourceAsset → Representation → SemanticCandidate → ContextBinding
        → ChangeSet → AuthorityDecision → Execution → Receipt
```

Five concerns that the transcript wedge kept too close together are now separate objects:

| Concern | Object | Today (shipped) | Target |
| --- | --- | --- | --- |
| What entered | `Capture` + `SourceAsset` | Durable `Capture` aggregate holding immutable `SourceAsset`s, written on every intake and backfilled from the legacy `LlmRequest` rows (CF-01); the queue row is now the job record | Same, once CF-03 retires the queue row: typed/pasted text is an inline asset, corrections supersede rather than rewrite, `SourceArtefact` adapts behind `LegacyArtefactId` |
| What Taskdeck derived | `Representation` (+ typed payloads) | `Transcript`, `ArtefactExtraction` | Header façade over both (CF-06 — draft contract), OCR/description payloads later |
| Where it belongs | `ContextBinding` | board required by `CaptureTriageService` and `ChatService` | Resolver at change-planning time (CF-09); boardless understanding |
| What should change | `ChangeSet` = `AutomationProposal` | proposal operations, revisions, Preview == Apply | Unchanged; candidates compile into it (CF-08) |
| Who allowed it | `AuthorityDecision` + receipt | explicit approve, explicit execute (review-first) | Same, plus a named policy for exactly one reversible class after evidence (CF-22, stretch) |

### Capture state is three axes, not one enum

| Axis | Enum | Who writes it | Values |
| --- | --- | --- | --- |
| User disposition | `CaptureUserDisposition` | the person | Active · Kept · Archived (terminal; never erases outcomes) |
| Processing summary | `CaptureProcessingSummary` | the CF-03 runner, from job/run records (a projection column) | Idle · Processing · Partial · Ready · Failed |
| Action state | `CaptureActionState` | planning records — candidates, bindings, change sets, receipts (a projection column) | Unplanned · NeedsInput · NeedsReview · Acted |

`Capture.Timeline` (`CaptureTimeline.Project`) is the one-line step the UI shows — *Received →
Preparing → Understood → Needs input / Needs review → Acted*, with *Kept*, *Failed*, *Archived* as resting
states. It is computed, never persisted as the only truth, so a kept, partially processed, already-acted
capture loses nothing. `Partial` renders as *Understood* with the failed leg listed beneath it.

## 2. Code map (scaffold PR `#2280` + reconciliation pass, 2026-08-30)

| Layer | File | Role |
| --- | --- | --- |
| Domain | `Enums/CaptureModality.cs`, `CaptureOriginAdapter.cs`, `CaptureProducerKind.cs` (Human · Agent · Integration), `CaptureIntentMode.cs` | The capture dimensions (ADR-0065 §Decision 2, amended): modality is per asset (`PrimaryModality` is a list summary); import is an origin, not a producer; intent is requested vs effective |
| Domain | `Enums/CaptureSourceMapping.cs` | Total forward mapping from the legacy `CaptureSource`; lossy reverse for compatibility readers; test enumerates the enum |
| Domain | `Enums/CaptureUserDisposition.cs` (+ `CaptureUserDispositionMapping`), `CaptureProcessingSummary.cs`, `CaptureActionState.cs`, `CaptureTimeline.cs` | The three state axes and the timeline projection |
| Domain | `Enums/RepresentationKind.cs`, `RepresentationQualityState.cs`, `EvidenceAnchorKind.cs`, `SemanticCandidateKind.cs`, `SemanticCandidateState.cs`, `ProcessingJobState.cs`, `ProcessorExecutionMode.cs`, `SourceAssetStorageKind.cs` | Vocabulary for CF-03/06/07/08/23 |
| Domain | `Processing/ProcessingCapability.cs` | The capability vocabulary; `Externalizable` vs `InProcessOnly` (`context.resolve`, `change.plan`, `change.verify` never leave the process) |
| Domain | `Entities/Capture.cs` | The durable aggregate: owner + `ProducedByPrincipalId`, `RequestedIntent` / `EffectiveIntent` / `IntentResolvedByRunId`, the three axes, `LegacySourceSnapshot`, up to 32 active `SourceAssets`; `FromQueueRequest` builds the ID-preserving row (sources and machine-derived axes seeded before the user disposition, so an archived legacy row still arrives with its material); `SupersedeInlineTextSource` / `CurrentText` / `ActiveSourceAssets` are the correction model |
| Domain | `Entities/SourceAsset.cs`, `SourceAssetTextPayload.cs` | One immutable input: modality, media type, SHA-256, size, storage kind (`InlineText` · `Blob` · `ExternalReference` · `LegacyArtefact`); text kept verbatim in its own row; `SupersedesAssetId` / `SupersededByAssetId` carry corrections without ever rewriting stored bytes |
| Domain | `Entities/CaptureBackfillState.cs` | The singleton marker that arms the Inbox read switch; the backfill itself needs no cursor |
| Domain | `Enums/CaptureLegacyStateMapping.cs` | Derives the three axes from a legacy queue row (status + proposal linkage + applied conversion + disposition); never a default `Received` |
| Application | `Services/CaptureIntakeService.cs` | **The one writer** of the aggregate: sources first (inline text + an `ExternalReference` asset when the payload carries one), then the queue row as the job record; called by `CaptureService.CreateAsync`, `LlmQueueService.AddToQueueAsync` and the backfill through `BuildCapture` |
| Application | `Services/CaptureBackfillService.cs` | The ID-preserving backfill: anti-join backlog (idempotent + resumable), batch-committed with its marker, a row it cannot map is skipped and keeps its queue-row reading |
| Application | `Interfaces/ICaptureStore.cs` | Persistence façade (implemented: `EfCaptureStore`, aggregate-aware). Owner-scoped throughout: detached `GetByIdForUserAsync` and `GetByIdsForUserAsync` for reads, tracked `GetByIdForUpdateAsync` + `UpdateAsync` so aggregate mutators commit through the unit of work, set-based erasure |
| Application | `Interfaces/ICaptureBackfillStore.cs` | The backlog anti-join and the completion marker (implemented: `EfCaptureBackfillStore`) |
| Application | `Interfaces/IRepresentationStore.cs`, `Interfaces/IBlobStore.cs` | **Draft** contracts for CF-06 / CF-23 — reference-semantics blob store; representation header with quality state and a migration-window nullable `CaptureId`; **no implementation registered** |
| Application | `Processing/ProcessorManifest.cs`, `ProcessorManifestValidator.cs`, `Processing/Schemas/processor-manifest.v1.schema.json`, `whisperx-processor.example.json` | Processor self-description with per-capability `capabilityContracts`; strict kebab-case enums; schema + canonical example embedded and read by the tests |
| Application | `Processing/Protocol/WorkerProtocol.cs` | Taskdeck Worker Protocol **v1-alpha** envelopes + structural validator (`docs/architecture/WORKER_PROTOCOL_V1.md`) |
| Application | `Services/ContextFabricSettings.cs` (`DualWriteCaptures`, `BackfillCaptures`, `ReadCapturesFromStore` — all default `true` since CF-01) | Migration switches; each one off falls back to the queue row |
| Infrastructure | `Persistence/Configurations/CaptureConfiguration.cs`, `SourceAssetConfiguration.cs`, `SourceAssetTextPayloadConfiguration.cs`, `CaptureBackfillStateConfiguration.cs`; migrations `20260830034447_AddCaptureAggregate`, `20260830141427_ReconcileContextFabricScaffold`, `20260830172044_AddCaptureBackfillState`; `Repositories/EfCaptureStore.cs`, `EfCaptureBackfillStore.cs`; `Persistence/ContextFabricBootstrap.cs` (runs the backfill after `SerializedMigrator` on every host) | The `Captures`, `SourceAssets`, `SourceAssetTextPayloads`, `CaptureBackfillStates` tables |
| Tests | `Domain.Tests/CaptureTests.cs`, `CaptureTimelineTests.cs`, `SourceAssetTests.cs`, `CaptureSourceMappingTests.cs`, `CaptureSourceSupersessionTests.cs`, `CaptureLegacyStateMappingTests.cs`, `ProcessingCapabilityTests.cs`; `Application.Tests/Processing/*`, `Services/CaptureServiceDualWriteTests.cs`, `LlmQueueServiceDualWriteTests.cs`, `CaptureBackfillServiceTests.cs`, `CaptureServiceReadSwitchTests.cs`, `CaptureIntakeSourceTests.cs`; `Api.Tests/ContextFabricCaptureBackfillTests.cs` (golden path over a seeded legacy queue), `MigrationBootstrapTests` (tables present + store round-trips on SQLite); `Architecture.Tests/CaptureIntakeSingleWriterTests.cs` (nothing but the intake constructs a `Capture`) | Proving checks |

Unchanged and still authoritative: `CaptureRequestContract` and the queue lanes (ADR-0045), `Transcript`
and `ProvenanceEvidenceLink` (ADR-0045 §7), `SourceArtefact` / `ArtefactExtraction` (ADR-0046),
`AutomationProposalService` and the operation vocabulary.

## 3. Capabilities

`content.inspect`, `text.normalize`, `document.extract-text`, `image.ocr`, `image.describe`,
`audio.preprocess`, `audio.transcribe`, `audio.align`, `audio.diarize`, `semantic.extract`,
`context.resolve`, `change.plan`, `change.verify`.

Representation-producing capabilities (`ProcessingCapability.RepresentationProducing`) may never touch
work state; `semantic.extract` produces a typed candidate batch; `context.resolve` binds targets;
`change.plan` compiles candidates into proposal operations; `change.verify` checks preconditions before
execution. **Externalizable** (may run in a sidecar or remote processor): everything up to and including
`semantic.extract`. **In-process only:** `context.resolve`, `change.plan`, `change.verify` — plus
authority evaluation and execution, which are not capabilities at all — because they need live domain
state, permissions, policy and concurrency semantics. The manifest validator rejects a sidecar or remote
manifest that declares an in-process capability.

## 4. Policy families (independent by design, visibly distinct vocabularies)

| Family | Controls | Presets | Issue |
| --- | --- | --- | --- |
| Processing profile | egress class, providers/regions, device use, quality vs latency, budgets, escalation, retention, vocabulary | Private · Balanced (default, one-time consent before any remote processor) · **Strict** (renamed from *Controlled* on 2026-08-30 so it cannot be confused with the *Control* presentation) · Expert | CF-10 |
| Authority profile | what may change, where, under which evidence and risk | exactly ADR-0057's Observe · Suggest · Assist · Operate · Autonomous · Custom — **none of it exists in code yet**: there is no authority profile, no `AuthorityDecision`, no `ExecutionReceipt`; review-first (explicit approve, explicit execute) is the only shipped policy | CF-22 (stretch; own gate) |
| Presentation profile | how much machinery is visible | Flow · Guided (default for new users) · Control — replaces `guided / workbench / agent` (`#1972`). Retiring the *Agent* **selector** (byte-identical to Workbench) retires nothing else: Agents, Runs, agent attribution and agent capabilities stay | CF-21 |

## 5. Slices, issues, and the v0.4 gates

| Slice | Issues | Milestone |
| --- | --- | --- |
| 0 ratify + measure | CF-00 `#2254`; **CF-24A** `#2319` corpus + benchmark command (v0.5, before CF-13/CF-15); **CF-24B** `#2277` runtime metrics + Control dashboard (v0.6) | v0.4 / v0.5 / v0.6 |
| 1 durable Capture + SourceAsset | CF-01 `#2255` (builds on this reconciliation), CF-02 `#2256` | v0.4 |
| 2 jobs and runs | CF-03 `#2257` (also defines the minimal `ProcessingPolicySnapshot` CF-14 needs), CF-05 `#2259` | v0.4 |
| 3 representations + anchors | CF-06 `#2260`, CF-07 `#2261` | v0.4 |
| storage seam | CF-23 `#2276` (reference semantics) | v0.4 |
| 4 candidates | CF-08 `#2262` (umbrella — split into one-PR children before `Now`) | v0.5 |
| 5 registry + routing | CF-04 `#2258` — v0.4 because the ADR-0048 worker `#1429` is its first sidecar; umbrella, split before `Now`; the protocol stays v1-alpha until PdfPig **and** WhisperX pass conformance; CF-10 `#2264`, CF-11 `#2265`, CF-15 `#2269`, CF-18 `#2272`, GEN-03 `#1317` | v0.4 → v0.6 |
| 6 Universal Capture + resolver + review | CF-09 `#2263`, CF-20 `#2273` (umbrella; hosts CF-16's `VoiceCapturePanel`), CF-21 `#2274` | v0.5 |
| voice vertical | CF-12 `#2266`, CF-13 `#2267` (fixtures from CF-24A `#2319`; the **accessible** route the v0.5 gate needs), CF-14 `#2268` (explicit per-run configuration, no CF-10 dependency), CF-16 `#2270` (reusable `useVoiceRecording` + `VoiceCapturePanel`), CF-17 `#2271` | v0.5 / v0.6 |
| 7 delegated authority | CF-22 `#2275` — **stretch / blocked, release-blocker = false** | v0.6 |
| 8 hosted scale | object-store `IBlobStore`, durable queue, GPU workers | v0.9, only on measured demand (ADR-0061 stage 3) |

**The first credible vertical (valid order):**

```text
reconciliation pass (this) → CF-01 durable Capture + SourceAsset foundation
    → { CF-02 dimensions · CF-03 jobs/runs · CF-23 blob store }
    → { CF-04 worker host · CF-06 representations · CF-12 audio source }
    → { CF-05 semantic.extract · CF-07 anchors }
    → CF-14 WhisperX dogfood route and/or CF-13 lightweight user route
    → CF-16 voice UX + audio evidence → existing proposal / review / apply
```

**v0.4 internal gates** (public registration last; milestone membership alone makes no child a release
blocker):

| Gate | Proves | Content |
| --- | --- | --- |
| A — Fabric persistence | the data model | Capture, SourceAsset, jobs/runs, representations, anchors, blob semantics (CF-01/02/03/06/07/23) |
| B — Processor containment | the boundary | Worker Protocol proven with PdfPig through `#1429`; security and resource caps (CF-04) |
| C — Trusted hosted instance | the operator | backup/restore, key custody, private users, operating runbook (ADR-0061 stage 1, `#1772`) |
| D — Public hosted beta | strangers | untrusted-user threat model, registration, cost ceilings, incident path (`#2243`) |

**v0.5 gate:** the first vertical live-verified **and** one speech route genuinely accessible to an
ordinary user — CF-13's lightweight route one-click, downloaded-on-enable or bundled, or a consented
managed route. WhisperX through a manually configured Python/CUDA environment is a dogfooding route,
not the public "Speak" promise.

## 6. Rules an implementer must not break

1. A capture is valid the moment its source assets are stored; no job failure may make it unreadable,
   and one failed asset never fails a capture whose other assets succeeded (`Partial`).
2. Migrations are ID-preserving (`Capture.Id = LlmRequest.Id`); nothing renumbers.
3. Source assets are immutable; typed and pasted text is a `SourceAsset`, never job state and never
   the capture's note. Derived normalised text is a representation.
4. Capture state is three axes; the timeline is a projection. Never reintroduce a single lifecycle enum.
5. Every capture creation path writes the aggregate through `CaptureIntakeService`.
6. Processors have no mutation tools; content is data, never instructions (GEN-09 `#1323`); sidecars
   and remote processors declare only externalizable capabilities.
7. Router v1 is constraints + ordered preference + a persisted route receipt; no scoring before the
   CF-24A corpus exists.
8. Review-first stays the only shipped authority policy until CF-22 passes its own gate.
9. Blobs stay in SQLite locally; a blob object is deleted only when its last reference is released;
   object storage only under ADR-0061 stage 3.
10. No new `CaptureSource` values, no new request-type lane predicates.

## 7. Verify

```text
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~CaptureTests|FullyQualifiedName~CaptureSourceMappingTests|FullyQualifiedName~ProcessingCapabilityTests|FullyQualifiedName~SourceAssetTests|FullyQualifiedName~CaptureTimelineTests"
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Processing|FullyQualifiedName~CaptureService|FullyQualifiedName~LlmQueueService|FullyQualifiedName~AccountDeletion"
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~MigrationBootstrapTests|FullyQualifiedName~McpApplicationServiceRegistrationTests"
dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1
```
