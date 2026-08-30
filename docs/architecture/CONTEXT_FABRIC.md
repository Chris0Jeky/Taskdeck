# Context Fabric — architecture reference

Last Updated: 2026-08-30

**Authority:** ADR-0065 (`docs/decisions/ADR-0065-context-fabric-capture-representation-processing.md`),
whose acceptance conditions were ruled under the maintainer's 2026-08-30 delegation on tracker CF-00
(`#2254`). This page is the agent-facing map of the architecture: what is shipped, what the scaffold
PR (`#2280`) adds, what each CF issue adds, and where the seams are. Shipped truth stays in
`docs/STATUS.md`.

## 1. The invariant

```text
Capture → SourceAsset → Representation → SemanticCandidate → ContextBinding
        → ChangeSet → AuthorityDecision → Execution → Receipt
```

Five concerns that the transcript wedge kept too close together are now separate objects:

| Concern | Object | Today (shipped) | Target |
| --- | --- | --- | --- |
| What entered | `Capture` + `SourceAsset` | `LlmRequest` row with a `CapturePayloadV1` JSON payload; `SourceArtefact` + `ArtefactBlob` | Durable `Capture` aggregate (scaffolded), `SourceArtefact` evolving into the source asset |
| What Taskdeck derived | `Representation` (+ typed payloads) | `Transcript`, `ArtefactExtraction` | Header façade over both (CF-06), OCR/description payloads later |
| Where it belongs | `ContextBinding` | board required by `CaptureTriageService` and `ChatService` | Resolver at change-planning time (CF-09); boardless understanding |
| What should change | `ChangeSet` = `AutomationProposal` | proposal operations, revisions, Preview == Apply | Unchanged; candidates compile into it (CF-08) |
| Who allowed it | `AuthorityDecision` + receipt | explicit approve, explicit execute (review-first) | Same, plus a named policy for exactly one reversible class after evidence (CF-22) |

## 2. Code map (scaffold PR `#2280`, 2026-08-30)

| Layer | File | Role |
| --- | --- | --- |
| Domain | `Enums/CaptureModality.cs`, `CaptureOriginAdapter.cs`, `CaptureProducerKind.cs`, `CaptureIntentMode.cs` | The four independent capture dimensions (ADR-0065 §Decision 2) |
| Domain | `Enums/CaptureSourceMapping.cs` | Total forward mapping from the legacy `CaptureSource`; lossy reverse for compatibility readers; test enumerates the enum |
| Domain | `Enums/CaptureLifecycleState.cs` (+ `CaptureLifecyclePolicy`) | User-legible capture timeline, independent of job state |
| Domain | `Enums/RepresentationKind.cs`, `EvidenceAnchorKind.cs`, `SemanticCandidateKind.cs`, `SemanticCandidateState.cs`, `ProcessingJobState.cs`, `ProcessorExecutionMode.cs` | Vocabulary for CF-03/06/07/08 |
| Domain | `Processing/ProcessingCapability.cs` | The capability vocabulary; manifests may declare only these |
| Domain | `Entities/Capture.cs` | The durable aggregate; `FromQueueRequest` builds the ID-preserving mirror |
| Application | `Interfaces/ICaptureStore.cs` | Persistence façade (implemented: `EfCaptureStore`) |
| Application | `Interfaces/IRepresentationStore.cs`, `Interfaces/IBlobStore.cs` | Contracts fixed for CF-06 / CF-23; **no implementation registered yet** |
| Application | `Processing/ProcessorManifest.cs`, `ProcessorManifestValidator.cs`, `Processing/Schemas/processor-manifest.v1.schema.json` | Processor self-description and its rules |
| Application | `Processing/Protocol/WorkerProtocol.cs` | Taskdeck Worker Protocol v1 envelopes + structural validator (`docs/architecture/WORKER_PROTOCOL_V1.md`) |
| Application | `Services/ContextFabricSettings.cs` (`ContextFabric:DualWriteCaptures`, default `false`) | Migration switches; `CaptureService` mirrors new captures when on |
| Infrastructure | `Persistence/Configurations/CaptureConfiguration.cs`, migration `20260830034447_AddCaptureAggregate`, `Repositories/EfCaptureStore.cs` | The `Captures` table (empty on an unchanged install) |
| Tests | `Domain.Tests/CaptureTests.cs`, `CaptureSourceMappingTests.cs`, `ProcessingCapabilityTests.cs`; `Application.Tests/Processing/*`, `Services/CaptureServiceDualWriteTests.cs`; `MigrationBootstrapTests` (table present) | Proving checks for the scaffold |

Unchanged and still authoritative: `CaptureRequestContract` and the queue lanes (ADR-0045), `Transcript`
and `ProvenanceEvidenceLink` (ADR-0045 §7), `SourceArtefact` / `ArtefactExtraction` (ADR-0046),
`AutomationProposalService` and the operation vocabulary.

## 3. Capabilities

`content.inspect`, `text.normalize`, `document.extract-text`, `image.ocr`, `image.describe`,
`audio.preprocess`, `audio.transcribe`, `audio.align`, `audio.diarize`, `semantic.extract`,
`context.resolve`, `change.plan`, `change.verify`.

Representation-producing capabilities (`ProcessingCapability.RepresentationProducing`) may never touch
work state; `semantic.extract` produces candidates; `context.resolve` binds targets; `change.plan`
compiles candidates into proposal operations; `change.verify` checks preconditions before execution.

## 4. Policy families (independent by design)

| Family | Controls | Presets | Issue |
| --- | --- | --- | --- |
| Processing profile | egress class, providers/regions, device use, quality vs latency, budgets, escalation, retention, vocabulary | Private · Balanced (default, one-time consent before any remote processor) · Controlled · Expert | CF-10 |
| Authority profile | what may change, where, under which evidence and risk | exactly ADR-0057's Observe · Suggest · Assist · Operate · Autonomous · Custom — **none of it exists in code yet**: there is no authority profile, no `AuthorityDecision`, no `ExecutionReceipt`; review-first (explicit approve, explicit execute) is the only shipped policy | CF-22 (first slice, own gate) |
| Presentation profile | how much machinery is visible | Flow · Guided (default for new users) · Control — replaces `guided / workbench / agent` (`#1972`) | CF-21 |

## 5. Slices and issues

| Slice | Issues | Milestone |
| --- | --- | --- |
| 0 ratify + measure | CF-00 `#2254` (v0.4); CF-24 `#2277` (v0.6 — the corpus needs the voice vertical's fixtures to exist first) | v0.4 / v0.6 |
| 1 durable Capture | CF-01 `#2255`, CF-02 `#2256` | v0.4 |
| 2 jobs and runs | CF-03 `#2257`, CF-05 `#2259` | v0.4 |
| 3 representations + anchors | CF-06 `#2260`, CF-07 `#2261` | v0.4 |
| storage seam | CF-23 `#2276` | v0.4 |
| 4 candidates | CF-08 `#2262` | v0.5 |
| 5 registry + routing | CF-04 `#2258` — pulled into **v0.4** because the ADR-0048 worker `#1429` (already v0.4) is its first sidecar; it is the one v0.4 item that is *not* behaviour-preserving (it launches supervised processes); CF-10 `#2264`, CF-11 `#2265`, CF-15 `#2269`, CF-18 `#2272`, GEN-03 `#1317` | v0.4 → v0.6 |
| 6 Universal Capture + resolver + review | CF-09 `#2263`, CF-20 `#2273`, CF-21 `#2274` | v0.5 |
| voice vertical | CF-12 `#2266`, CF-13 `#2267`, CF-14 `#2268`, CF-16 `#2270`, CF-17 `#2271` | v0.5 / v0.6 |
| 7 delegated authority | CF-22 `#2275` | v0.6 (gated) |
| 8 hosted scale | object-store `IBlobStore`, durable queue, GPU workers | v0.9, only on measured demand (ADR-0061 stage 3) |

The first credible vertical: **CF-01 → CF-12 → CF-14 → CF-06/07 → existing triage → existing proposal
compiler → CF-16** (voice note → time-anchored transcript → reviewable proposal → approve → apply →
audio evidence playback).

## 6. Rules an implementer must not break

1. A capture is valid the moment its source is stored; no job failure may make it unreadable.
2. Migrations are ID-preserving (`Capture.Id = LlmRequest.Id`); nothing renumbers.
3. Processors have no mutation tools; content is data, never instructions (GEN-09 `#1323`).
4. Router v1 is constraints + ordered preference + a persisted route receipt; no scoring before the
   CF-24 corpus exists.
5. Review-first stays the only shipped authority policy until CF-22 passes its own gate.
6. Blobs stay in SQLite locally; object storage only under ADR-0061 stage 3.
7. No new `CaptureSource` values, no new request-type lane predicates.

## 7. Verify

```text
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~CaptureTests|FullyQualifiedName~CaptureSourceMappingTests|FullyQualifiedName~ProcessingCapabilityTests"
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Processing|FullyQualifiedName~CaptureService"
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~MigrationBootstrapTests"
dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1
```
