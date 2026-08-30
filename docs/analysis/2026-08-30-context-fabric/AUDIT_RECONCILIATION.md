# External audit of PR #2280 — disposition (2026-08-30 reconciliation pass)

Last Updated: 2026-08-30

**What this is.** The maintainer handed the agent pass an external LLM audit of the Context Fabric
scaffold (`EXTERNAL_AUDIT_2026-08-30_AS_RECEIVED.md`) with the instruction to implement it. This
record is the disposition: every finding, what was done with it, and where it landed. Decision
text lives in ADR-0065 §*Amendments (2026-08-30)*; the code is PR `#2320`
(branch `issue-2254/context-fabric-reconciliation`); the tracker changes are on CF-00 `#2254` and the
child issues. The pass was **behaviour-preserving**: `ContextFabric:DualWriteCaptures` stays off
by default and every new table is empty on an unchanged install.

## 1. Architecture corrections

| # | Finding | Disposition | Where |
| --- | --- | --- | --- |
| 1 | No general `SourceAsset` between `Capture` and `Representation`; typed text had no home once `LlmRequest.Payload` stops being capture state | **Accepted, implemented.** `SourceAsset` (+ `SourceAssetTextPayload`) with modality, media type, content hash, byte size, storage kind (`InlineText` · `Blob` · `ExternalReference` · `LegacyArtefact`), blob reference, soft legacy-artefact reference, external reference, original name; a capture holds up to 32 assets in order. Typed/pasted text is an immutable inline asset (verbatim, hash over UTF-8, cap = the shipped 200k transcript cap). `SourceArtefact`/`ArtefactBlob` are adapted behind the model (`LegacyArtefactId`), not replaced. CF-01 now owns the foundation explicitly | `Domain/Entities/SourceAsset.cs`, `SourceAssetTextPayload.cs`, migration `ReconcileContextFabricScaffold`, `DATA_MODEL.md`, CF-01 `#2255` |
| 2 | `CaptureLifecycleState` fused user disposition, processing state, action state and failure into one enum | **Accepted, implemented.** Three orthogonal axes — `CaptureUserDisposition` (Active · Kept · Archived), `CaptureProcessingSummary` (Idle · Processing · Partial · Ready · Failed; a projection column CF-03 rewrites from job records), `CaptureActionState` (Unplanned · NeedsInput · NeedsReview · Acted; a projection from planning records) — and `CaptureTimeline.Project` as the one-line timeline the UI shows, never persisted as the only truth. Archived is terminal but does not erase outcomes; `Partial` never renders as failed. The enum and its policy are deleted | `Domain/Enums/CaptureUserDisposition.cs`, `CaptureProcessingSummary.cs`, `CaptureActionState.cs`, `CaptureTimeline.cs`, `Capture.cs` |
| 3 | Worker Protocol frozen as v1 while unable to return candidates, bindings, structured data or geometry; one input only; speech-specific options; global `outputSchemas`; thin usage | **Accepted, implemented as v1-alpha.** Typed multi-input (`source-asset` · `representation` · `context-snapshot`); output union `representation` · `candidate-batch` · `diagnostic` dispatched on `type` in any member order; representation payloads typed by kind (text + segments + regions, or `structured`); candidate evidence with typed anchors; usage with tokens, pages, bytes, billable units, estimated cost; per-capability `capabilityContracts` in the manifest; sidecars/remotes limited to `ProcessingCapability.Externalizable` (`context.resolve`, `change.plan`, `change.verify` stay in-process). Draft until PdfPig (`#1429`) and WhisperX (CF-14) both pass CF-04 conformance | `Application/Processing/Protocol/WorkerProtocol.cs`, `ProcessorManifest*.cs`, `processor-manifest.v1.schema.json`, `WORKER_PROTOCOL_V1.md`, CF-04 `#2258` |
| 4 | `IBlobStore` deleted by hash; media type on the object; no quota reservation; stream ownership unclear; `byte[]` is not streaming | **Accepted, implemented.** Object + reference model: `AcquireAsync` (quota reserved for an expected size before the stream is read; per-owner dedupe returns a reference to the existing object), `AcquireExistingAsync`, `ReleaseAsync(referenceId, ownerUserId)` (owner-scoped; delete on the last reference, in the ambient transaction), `OpenReadAsync`, `FindByHashAsync`, `GetUsageAsync` (per-modality). Media type lives on the asset. Stream ownership documented; the incremental-BLOB / chunk-row requirement recorded for CF-23. SQLite confirmed as the local implementation | `Application/Interfaces/IBlobStore.cs`, CF-23 `#2276` |
| 5 | Producer identity: `Import` is a transport; no producer principal; `Auto` is not an effective intent; `PrimaryModality` is a summary; `LegacySource` is a snapshot | **Accepted, implemented.** `CaptureProducerKind` = Human · Agent · Integration (Import removed; import sources map to Human over the Import origin); `ProducedByPrincipalId?` beside the owner `UserId`; `RequestedIntent` / `EffectiveIntent?` / `IntentResolvedByRunId?` with `ResolveIntent` only for an Auto request; `PrimaryModality` documented as the first asset's summary; `LegacySourceSnapshot` | `Capture.cs`, `CaptureProducerKind.cs`, `CaptureIntentMode.cs`, `CaptureSourceMapping.cs` |
| 6 | `IRepresentationStore` called "fixed" with a non-nullable `CaptureId` and no quality state | **Accepted, implemented as a draft.** Descriptor gains `UserId`, `QualityState` (Provisional · Final · Verified · Superseded), `SupersededByRepresentationId`; `CaptureId` nullable only for the migration window with the target of a backfilled Capture for every retained legacy source; six invariants CF-06 must prove listed on the interface | `Application/Interfaces/IRepresentationStore.cs`, `RepresentationQualityState.cs`, CF-06 `#2260` |
| 7 | `LlmQueueService.AddToQueueAsync` bypassed dual-write | **Accepted, implemented.** `CaptureIntakeService` is the single writer of the aggregate; `CaptureService.CreateAsync` and the enqueue path both call it (mirror + inline text asset, same unit of work; no-op while the flag is off). CF-01 extends it into the native intake | `Application/Services/CaptureIntakeService.cs`, `CaptureService.cs`, `LlmQueueService.cs`, `LlmQueueServiceDualWriteTests` |

## 2. Issue and milestone graph

| Finding | Disposition | Where |
| --- | --- | --- |
| Headline first vertical contradicted the dependency table | **Accepted.** Order is now: reconciliation pass → CF-01 (Capture + SourceAsset) → {CF-02, CF-03, CF-23} → {CF-04, CF-06, CF-12} → {CF-05, CF-07} → CF-14 and/or CF-13 → CF-16 → existing proposal/review/apply | CF-00 `#2254`, `CONTEXT_FABRIC.md` §5, `REVIVAL_PLAN.md` Phase 5 |
| CF-24 scheduled after work that needs its fixtures | **Accepted.** CF-24A `#2319` (committed corpus + reproducible benchmark command, v0.5, before CF-13/CF-15) opened; `#2277` retitled CF-24B (runtime metrics + Control dashboard, v0.6) | CF-24A `#2319`, `#2277` |
| CF-14 hid a dependency on CF-10 profiles | **Accepted.** CF-14 uses explicit per-run configuration through a minimal `ProcessingPolicySnapshot` that CF-03 defines; CF-10 takes control later | `#2268`, `#2257` |
| CF-20 would rebuild CF-16 | **Accepted.** CF-16 delivers a reusable `useVoiceRecording` composable + `VoiceCapturePanel` with the upload/retry state machine; CF-20 hosts it | `#2270`, `#2273` |
| CF-22 must not block v0.6 | **Accepted.** Stretch / blocked, release-blocker = false; evidence gate amended (ruling 6) | `#2275`, milestone 7 description |
| v0.4 carries four theses | **Accepted.** Internal gates A (Fabric persistence) · B (processor containment) · C (trusted hosted instance) · D (public hosted beta), public registration last; milestone membership alone makes no child a release blocker | `PRODUCT_DIRECTION.md`, `REVIVAL_PLAN.md`, milestone 5 description |
| v0.5 needs one genuinely accessible speech route | **Accepted.** Added to the v0.5 gate and to CF-13's acceptance (one-click / downloaded-on-enable / bundled, or a consented managed route) | `PRODUCT_DIRECTION.md`, `#2267` |
| CF-04, CF-08, CF-20 are epics | **Accepted in part.** Kept as umbrellas; each body now carries its proposed one-PR child slices and the rule "split before `Now` admission". Children are not opened yet — the REVIVAL §5 queue stays finite until admission | `#2258`, `#2262`, `#2273` |

## 3. Rulings

| # | Audit | Disposition |
| --- | --- | --- |
| 1, 3, 5, 7, 8 | Confirm | **Confirmed** as ruled |
| 2 | Confirm with gates | **Confirmed with amendment** — gates A–D, release-blocker classification |
| 4 | Confirm principle, amend interface | **Confirmed with amendment** — reference semantics, quota reservation, media type on the asset |
| 6 | Amend the evidence gate | **Amended** — the ≥50 / ≥90% / zero-reversal figures are provisional orientation numbers; CF-22 stays blocked on a risk-based shadow-and-canary report (target-board accuracy, permission/ownership accuracy, unchanged-acceptance rate, false-action rate, correct no-action rate, compensation/undo reliability, zero cross-user/cross-board violations, shadow-policy results before any real auto-execution, an initial daily operation ceiling, an immediate kill switch) and the maintainer's explicit go |
| 9 | Confirm with one caveat | **Confirmed with amendment** — retiring the *Agent* workspace-mode selector (byte-identical to Workbench, `#1972`) does not remove Agents, Runs, agent attribution or agent capabilities; the three policy vocabularies stay visibly separate; the processing preset *Controlled* is renamed **Strict** so it cannot be confused with the *Control* presentation profile |

The confirmation is recorded on CF-00 `#2254` as the maintainer's reply (posted by the agent pass on
the maintainer's instruction to implement the audit; the ruling text is the audit's recommended
confirmation). ADR-0065's status is now *Accepted (confirmed 2026-08-30 with amendments)*.

## 4. Smaller cleanup findings

| Finding | Disposition |
| --- | --- |
| `context-fabric` label says ADR-0064 | **Fixed** (label description → ADR-0065) |
| CF-01 comment claims account deletion misses Captures | **Annotated resolved** on `#2255` (`AccountDeletionService` deletes the mirrors inside the erasure transaction; `EfCaptureStore.DeleteByUserAsync` now also removes assets and payloads) |
| CF-04 residuals only in comments | **Promoted into CF-04's acceptance checklist.** Already done in this pass: `Enum.GetNames` validation, null-warning rejection, notification-envelope validator, `ProcessorCancelParams`, exact kebab-case enum spellings, schema/validator parity for `costModel.type`, example manifest embedded and read by the tests. Still open in the checklist: cancellation grace, per-process session secret, `concurrent-jobs` feature honouring, the JSON-schema-library decision |
| "Accepted under delegation" as a permanent status | **Fixed** — ADR-0065 is *Accepted (confirmed 2026-08-30 with amendments)*; the delegation history stays in the record |
| "§I — 35 open" in the handoff was misleading | **Noted** — 35 is the whole file's open count; §I carried two rows. The CF-00 row is now checked (the confirmation exists); the CF-22 gate row stays open |

## 5. Declined or deferred

| Item | Why |
| --- | --- |
| Renaming `UserId` to `OwnerPrincipalId` | Every owned entity in the repository uses `UserId`; the semantics are documented on `Capture.UserId` and `ProducedByPrincipalId` carries the distinction the audit wanted. Rename would be churn without a second owner kind |
| Opening the CF-04 / CF-08 / CF-20 child issues now | The wave is admitted through REVIVAL_PLAN §5's finite queue; the children are listed on each umbrella and are opened at admission, not before |
| A `ProcessingJob` / `ProcessingRun` scaffold | CF-03's scope; the audit did not ask for it and the processing-summary axis is explicitly a projection those records will own |

## 6. Verification of this pass

Domain `Capture|CaptureSourceMapping|ProcessingCapability|SourceAsset|CaptureTimeline` filter 76/76;
Application `Processing|CaptureService|LlmQueueService|AccountDeletion|CaptureIntake` filter 199/199;
Api `MigrationBootstrapTests|McpApplicationServiceRegistrationTests|DataPortability|AccountDeletion`
32/32 (includes the new `EfCaptureStore` round-trip on the migrated SQLite schema and
`HasPendingModelChanges() == false`); Architecture 26/26 (1 pre-existing skip); docs gates green.
Not verified: the dual-write flag on a live host beyond the SQLite round-trip test; the frontend
(untouched).
