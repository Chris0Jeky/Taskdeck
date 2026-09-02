# CF-02 — Split capture dimensions: modality, origin adapter, producer, intent (#2256)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, ADR-0065 (accepted under delegation, amended 2026-08-30), `docs/architecture/CONTEXT_FABRIC.md` and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

Stop `CaptureSource` being the model. The four dimensions already persist on `Capture`; what is
missing is that **the server decides the producer** instead of trusting the client's `source`
string, that **new clients can send and read the dimensions** over the API, and that a legacy
`source` disagreeing with explicit dimensions **fails closed with a stable 400**. `CaptureSource`
survives only as `LegacySourceSnapshot`, and lane routing stops reading it at CF-05.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-01 `#2255` | **closed** (PR `#2344`, merge `a6cc459c9`) | predecessor, delivered | The four dimension columns, `CaptureSourceMapping`, `CaptureIntakeService` as sole writer, dual-write + read switch default on. `docs/STATUS.md` line 11–17 |
| CF-01b `#2345` | open | **precedes the DTO slice** | Provenance, suggestion metadata and the disposition receipt still live in `CapturePayloadV1`. Any new capture DTO field must not be a fourth read source while that split is open |
| CF-01c `#2347` | open | precedes any new disposition write path | `ApplyDurableDispositionAsync` stamps the aggregate newer than the queue row and masks a text divergence. Do not add a second stamping path before it lands |
| CF-05 `#2259` | open | consumer | Acceptance box 3 ("lane routing no longer reads `CaptureSource`") is *tracked there*, not closeable here |
| CF-03 `#2257` | open | consumer | `EffectiveIntent` may only be set by `Capture.ResolveIntent(effective, runId)`; no run exists to resolve it until CF-03 |
| CF-12 / CF-20 | open | consumers | Retention by modality; offline intake supplies `CapturedAtClient`, dead-by-construction today |

Grepped for callers of the shipped `producerOverride` / `producedByPrincipalId` parameters across
`backend/src`: **zero**. Producer is therefore always `CaptureSourceMapping.Resolve(payload.Source)`
today — i.e. derived from the client-selected `source`, which is exactly what the 2026-08-30 Codex
P2 on this issue forbids.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CF02-1-producer-stamping` | The authenticated principal decides `ProducerKind` / `ProducedByPrincipalId`; the MCP write path stamps `Agent` + agent-profile id, the connector path `Integration` + connector id, everything else `Human` + null | — | implementation | **Yes — start here.** The parameters already exist on `CaptureIntakeService.IntakeAsync`/`BuildCapture` with no callers; wiring them touches `Taskdeck.Api/Mcp/WriteTools.cs`, the connector intake and `CaptureService.CreateAsync` only. It needs nothing from `#2345` / `#2347` |
| `CF02-2-conflict-contract` | `CreateCaptureItemDto` accepts optional explicit dimensions; a legacy `source` inconsistent with them is rejected with `ErrorCodes.ValidationError` (400); consistent pairs and source-only clients are unchanged | 01 | implementation | No — the reject rule must know how the producer is stamped, or a client can force a producer through the "explicit" door |
| `CF02-3-additive-dtos` | `CaptureItemDto` / `CaptureItemSummaryDto` expose `PrimaryModality`, `OriginAdapter`, `ProducerKind`, `RequestedIntent`, `EffectiveIntent` additively; `Source` stays and stays correct | 02, CF-01b `#2345` | implementation | No — `#2345` decides where each capture field is read from; adding fields to the DTO first creates a second read-source problem to unwind |
| `CF02-4-per-asset-routing` | `ResolveRequestTypeForSource` stops being the capture-level lane decision; routing consults each `SourceAsset.Modality` | 03, CF-05 `#2259`, CF-03 `#2257` | implementation | No — the job/run substrate that would carry a per-asset lane does not exist |
| `CF02-5-legacy-retirement` | Ban canonical routing from `LegacySourceSnapshot`; keep it as a persisted compatibility snapshot only | 04 | cleanup | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Four dimension enums | `CaptureModality` (Text·Audio·Image·Document·Structured), `CaptureOriginAdapter` (9 members incl. `Mcp`, `Import`, `Integration`, `Api`), `CaptureProducerKind` (Human·Agent·Integration), `CaptureIntentMode` (Remember·Organize·Act·Auto) | **exists** | `backend/src/Taskdeck.Domain/Enums/` |
| Legacy → dimensions mapping | `CaptureSourceMapping.Resolve` / `.ToLegacySource` / `.IsAmbiguousLegacySource`, `CaptureDimensions` record struct | **exists**, total over all twelve `CaptureSource` values | `Enums/CaptureSourceMapping.cs`; enumerated by `backend/tests/Taskdeck.Domain.Tests/CaptureSourceMappingTests.cs` |
| Persisted columns | `Capture.PrimaryModality`, `.OriginAdapter`, `.ProducerKind`, `.ProducedByPrincipalId`, `.RequestedIntent`, `.EffectiveIntent`, `.IntentResolvedByRunId`, `.LegacySourceSnapshot` | **exists** | `Migrations/TaskdeckDbContextModelSnapshot.cs` `Captures` block; migration `20260830141427_ReconcileContextFabricScaffold` |
| Intent resolution guard | `Capture.ResolveIntent(effectiveIntent, resolvedByRunId)` | **exists** | Effective intent is never `Auto` and always carries a run id |
| Single writer | `CaptureIntakeService` (`IntakeAsync`, static `BuildCapture`), proven over the source tree by `CaptureIntakeSingleWriterTests` | **exists** | `Application/Services/CaptureIntakeService.cs` |
| Server-stamped producer | `producerOverride` / `producedByPrincipalId` parameters | **exists, unwired** | No caller in `backend/src`. This slice's core work |
| Dimensions on the capture API | — | **missing** | `CreateCaptureItemDto` takes `string? Source` only; `CaptureItemDto` exposes `CaptureSource Source` and no dimension. Grepped `PrimaryModality|OriginAdapter|RequestedIntent|EffectiveIntent|ProducerKind` across `Application/DTOs` + `Api`: the only hits are the export DTO in `DataPortabilityDtos.cs` (lines 106–110) |
| Inconsistent-pair rejection | — | **missing** | Nothing validates a legacy/new combination because no new input exists yet |
| Agent principal on the MCP path | `Taskdeck.Api/Mcp/WriteTools.cs:406–411` builds `CreateCaptureItemDto(..., Source: "Typed")` and calls `_captureService.CreateAsync` | **exists, wrong** | An MCP capture is recorded as a human typing in the web composer |
| Per-asset routing | `CaptureRequestContract.ResolveRequestTypeForSource(CaptureSource)` → `inbox.capture.v1` \| `inbox.capture.transcript.v1` | **exists, capture-level** | One lane per capture; a multi-asset capture cannot route per asset |
| Client capture timestamp | `Capture.CapturedAtClient` | **exists, always null** | No create path carries one (ADR-0065 §In force); CF-20 is the reason it is kept |

## Implementation plan

**Preflight.** Read `#2256` including its single 2026-08-30 comment (the producer-stamping rule is
there, not in the body), ADR-0065 §Decision 2 and §Amendments, and `docs/architecture/CONTEXT_FABRIC.md`
§ dimension table. Confirm `#2345` / `#2347` state — they change what a capture field's read source is,
which is the same seam slice 03 edits.

**Sequence.** 01 alone is a complete, shippable behaviour change with no predecessor. Then 02, then
03 after `#2345`. 04 and 05 wait on CF-05 and CF-03.

**Slice 01 detail.** The rule from the issue comment, restated as code: a client-created capture is
`Human` with `ProducedByPrincipalId = null`; `Agent` requires an agent-profile id (`AgentProfile`);
`Integration` requires a connector identity (`IntegrationConnector`). Derive it from the
authenticated principal — the MCP API-key claims (`ApiKeyMiddleware` sets
`taskdeck:mcp:api-key-scopes` and an `McpApiKeyId` item) and the connector's own identity — never
from `payload.Source`. Fail closed: a caller that asserts `Agent`/`Integration` without a resolvable
principal is a 400, not a silent downgrade to `Human`.

**Producer-owned paths:** `backend/src/Taskdeck.Api/Mcp/WriteTools.cs`,
`backend/src/Taskdeck.Application/Services/CaptureService.cs` (create path only),
`backend/src/Taskdeck.Application/DTOs/CaptureDtos.cs`, `CaptureContracts.cs`,
`backend/tests/Taskdeck.Application.Tests/Services/`, `backend/tests/Taskdeck.Api.Tests/`.

**Integration-owner seams:** `CaptureIntakeService.cs` (shared with CF-01b's read-source work),
`TaskdeckDbContext.cs`, `DataPortabilityDtos.cs`, `docs/STATUS.md`, `docs/architecture/CONTEXT_FABRIC.md`.

**Rollout / rollback.** Slice 01 changes *stored provenance*, not any read, so it needs no flag —
but it is not reversible by configuration, so its migration story is "existing rows keep the producer
the backfill derived; new rows are stamped". State that in the PR. Slices 02–03 are additive contract
changes: old clients that send only `source` must be byte-identical, which is the contract test.

**Definition of done.** No new enum values added to `CaptureSource` (ADR-0065 §Decision 2 stops that
growth; GEN-04 `#1318`'s `ImageUpload`/`PdfUpload`/`FileDrop` are explicitly not taken). Acceptance
box 3 stays open here and is closed on CF-05 `#2259`.

## Test plan

- [ ] Domain: `CaptureSourceMapping` round-trips every one of the twelve `CaptureSource` values and rejects an unknown token with `ErrorCodes.ValidationError` — already covered by `CaptureSourceMappingTests`; re-assert after any mapping edit. `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~CaptureSourceMapping"`
- [ ] Application: an MCP-originated capture persists `ProducerKind = Agent` with a non-null `ProducedByPrincipalId`, and `LegacySourceSnapshot` still reads back the value the queue-row contract expects
- [ ] Application: a connector-originated capture persists `Integration` + connector id; a plain web capture persists `Human` + null
- [ ] Application: a caller asserting `Agent`/`Integration` with no resolvable principal gets a stable 400 and **no** capture is written (fail closed, not downgrade)
- [ ] Api: an existing client sending only `source` gets a byte-identical `CaptureItemDto` before and after each slice — the contract test that guards slices 02 and 03
- [ ] Api: `source` + explicit dimensions that disagree → 400 with the shipped `ApiErrorResponse` shape; agreeing pair → 200 and the dimensions read back
- [ ] Api: `RequestedIntent = Auto` is accepted; `EffectiveIntent = Auto` is rejected at every entry point; `EffectiveIntent` set without a run id is rejected
- [ ] Persistence: `MigrationBootstrapTests` green; `HasPendingModelChanges() == false`
- [ ] Export: `DataPortabilityDtos` capture rows carry the stamped producer; export/import/delete round-trip unchanged
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Legacy `source` and explicit dimensions disagree — reject; never silently prefer one. The
  ambiguous four (`Paste`, `TranscriptPaste`, `WebClip`, `MarkdownImport` — `CaptureSourceMapping.IsAmbiguousLegacySource`)
  are where a "prefer the explicit value" shortcut would lose information.
- An integration-driven **import**: origin is `Import` (a transport), producer is `Integration` (a
  principal). The mapping alone yields `Human`, so only the intake override can get this right.
- Multi-asset capture: `PrimaryModality` is a summary of the first asset. Do not derive a lane from
  it — a text sentence plus an audio note routes as text and the audio is never transcribed.
- `EffectiveIntent` requested as `Auto` but no run ever resolves it — stays null forever; readers must
  render "not yet resolved", not "Auto".
- Account deletion of the agent profile or connector named by `ProducedByPrincipalId` — the column is
  a bare `Guid?` with no FK; decide tombstone vs null before slice 01 merges.
- An unknown enum token from a future client — must be a stable 400, never a default to `Typed`.
- The MCP path currently sends `Source: "Typed"`; changing the *producer* must not change the stored
  `LegacySourceSnapshot`, or every existing MCP capture's queue-row contract shifts.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Issue pack | Bundle `01_MILESTONE_5/issue-packs/2256.md` | The four child-PR shape (largely reusable) and the "avoid" list | Its "Reconciled current state" predates CF-01's merge; see corrections |
| Test vectors | `docs/analysis/2026-08-30-acceleration-bundle/testing/test-vectors/capture-backfill-cases.json` | Six of the ten rows are dimension cases (`markdown-import-human`, `integration-import-override`, `empty-text`, `malformed-json`, `idempotent-rerun`, `interrupted-resume`) | Rows 1–4 assert `ProcessingSummary` values that **already match** shipped `CaptureLegacyStateMapping.Resolve` (Completed→Ready, Pending→Idle, Processing→Processing, Failed→Failed) — regression material, not new work. `integration-import-override` is the only row that fails on `main`, and it is exactly slice 01 |
| Diagram | `.../diagrams/context-fabric-lifecycle.svg` (`.dot` beside it) | The single-writer / immutable-source / "processor failure never makes Capture unreadable" boundary drawing | Explanatory. It draws `SourceAsset → ProcessingJob` as the routing edge — correct target, not current state |
| Blueprint | `.../architecture/CONTEXT_FABRIC_IMPLEMENTATION_BLUEPRINT.md` §2 Capture aggregate, §6 index plan | The aggregate rules, restated compactly; the Capture/SourceAsset index candidates | Read its 2026-09-02 validation preface first — §3 migration train is largely shipped |
| Testing doc | `.../testing/MIGRATION_PROOF_CHECKLIST.md` | The additive-migration checklist for slice 03's DTO/column work | Generic; a floor, not this issue's coverage |

## Corrections to the bundle

1. **Pack says:** "Enums, mapping and reconciled columns exist in the scaffold." **True on `main`:**
   they exist on `main` and are *live* — CF-01 `#2255` merged (PR `#2344`, `a6cc459c9`), dual-write,
   backfill and the Inbox read switch all default **on** (`ContextFabricSettings`). **Consequence:**
   this is no longer a scaffold-completion slice; every change here runs against real rows.
2. **Pack's dependency line "Depends on: #2255":** stale. The live predecessors are **CF-01b `#2345`**
   (JSON-twin retirement) and **CF-01c `#2347`** (disposition-stamp divergence), which did not exist
   when the bundle was cut. Slice 03 depends on `#2345`; any new disposition write depends on `#2347`.
3. **Pack's "avoid" list includes `Import as producer`.** Correct and now enforced by the type system:
   `CaptureProducerKind` has only `Human · Agent · Integration`, and `Import` is a
   `CaptureOriginAdapter` member. The pack states it as a rule to remember; it is already a compile error.
4. **Pack's "avoid" list includes `EffectiveIntent=Auto`.** Also already enforced: `EffectiveIntent`
   is only settable through `Capture.ResolveIntent(effective, runId)`. The remaining risk is an API
   surface that lets a client set it, which is slice 02/03's job.
5. **Pack claims the remaining work is "intake/API stamping … and removing legacy-source routing after
   CF-05".** Incomplete. The *largest* unstated gap is that the shipped stamping hook has **no
   callers** — `producerOverride` and `producedByPrincipalId` are dead parameters, grepped across
   `backend/src`. Producer today is derived from the client's `source`, the precise failure the issue's
   own Codex comment names.
6. **Pack's CF02-2 "additive DTOs" is presented as independent.** It is not: `#2345` is still deciding
   which fields read from the aggregate versus the queue-row JSON. Landing new DTO fields first adds a
   third read source to a seam that is mid-migration.
7. **Pack's file-ownership globs list `backend/src/**/CaptureContracts*` and `Mcp*`/`Connectors*`.**
   Accurate, but it omits `backend/src/Taskdeck.Api/Mcp/WriteTools.cs`, which is the single concrete
   file where the MCP producer bug lives (`Source: "Typed"`, line 408).
8. **Pack's suggested-image block** tells the reader to add
   `![…](../path/to/context-fabric-lifecycle.svg)` to the issue. The diagram is now archived at
   `docs/analysis/2026-08-30-acceleration-bundle/diagrams/context-fabric-lifecycle.svg`; a relative
   path in a GitHub issue body does not resolve. Link the repo path or nothing.
9. **Vocabulary check:** clean. The pack uses `Remember`/`Act`/`Organize`/`Auto` and
   `Human`/`Agent`/`Integration` exactly as shipped, and never says "Controlled" (the retired preset
   name — the shipped presets are Private/Balanced/Strict/Expert).
