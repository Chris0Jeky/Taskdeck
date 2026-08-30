# Agent Index - Taskdeck (seam map)

Last-Verified: 2026-08-30 (Context Fabric seam re-verified after the reconciliation pass; the agent tooling / CI row re-verified the same day for the Smart CI scaffold, ADR-0066; other regions
unchanged — agent-inventory routing verified 2026-08-18; transcript/MCP/container and map-reduce seams
remain verified 2026-08-02; the rest retain the
2026-07-13 exploration). Re-verify when a seam moves; treat a stamp older than ~90 days as stale
(a wrong map misroutes — worse than no map).

This is the repo's seam map — a fast orientation layer for coding agents. It points to
interfaces, invariants, and verification commands; it does not duplicate implementation.
It is the Taskdeck equivalent of the harness `AGENT_MAP.md` (grandfathered name).

## Orient (do NOT bulk-read the big docs)

- **Start here** = this file. Find your region in the seams table, jump to its entry points.
- Region rules auto-load: each major directory has a scoped `CLAUDE.md` (`backend/`,
  `frontend/taskdeck-web/`, `scripts/agent_hooks/`) — Claude Code loads it when you touch files
  there. Read that, not the whole repo.
- Current shipped state: `docs/STATUS.md` (source of truth) — read the relevant section, it is
  ~1.5k lines; do not read it end-to-end. Roadmap: `docs/IMPLEMENTATION_MASTERPLAN.md` (~1.7k
  lines — also section-read only, never bulk-read).
- Contract: `AGENTS.md`. Invariants: `docs/GOLDEN_PRINCIPLES.md`. Skills: `.codex/skills/` for Codex and `.claude/skills/` for Claude.

## Do Not Read By Default

- `.claude/worktrees/`, `.worktrees/`, `frontend/taskdeck-web/node_modules/`
- build/coverage/dist outputs, Playwright traces, generated OpenAPI/compiled assets
- EF `**/Migrations/*.Designer.cs` snapshots (unless a migration is the task)
- `docs/agentic/failure_ledger.jsonl` (read the rendered `FAILURE_LEDGER.md` instead)
- `docs/archive/` and large packs under `docs/InReview/` / `docs/WIP/` unless reconciling them
- the sibling `agent-harness` checkout (the blueprint) — lives outside this repo, not under it

## Product And Engineering Seams

| Domain | Entry points | Invariants (load-bearing) | Verify |
| --- | --- | --- | --- |
| Capture → review → board | `Api/Controllers/CaptureController.cs`, `AutomationProposalsController.cs`; `Application/Services/LlmCaptureTriageExtractor.cs` and `TranscriptTriageChunking.cs`; `views/InboxView.vue`, `ReviewView.vue`, `composables/useReviewProposals.ts` | **Preview == Apply** (#1235: diff + executor both materialize the latest `ProposalRevision`); approve & execute are two explicit calls; execute needs an Idempotency-Key; a missing/mismatched proposal deep link never substitutes another actionable proposal; Paper post-decision receipts are board-scoped, terminal receipts are keymap-nonactionable, and only Approved offers explicit Apply; provenance server-stamped, client identity fields rejected; transcript triage uses the LLM-backed extractor only after kill-switch/provider-health/quota gates and otherwise records deterministic fallback honestly. Under-budget transcripts stay one provider call; only a budget-forced split maps chunks, and any failed map leg falls back for the complete capture. Provider-call progress renews the transcript-worker heartbeat, so readiness permits one selected-provider timeout but not an entire stuck map-reduce batch. | `TranscriptTriageChunkingTests`, `LlmCaptureTriageExtractorTests`, `TranscriptTriageLlmGoldenPathIntegrationTests`, `HealthApiTests`; capture/review unit + `CaptureApiTests`, `ProposalRevisionApiTests`; E2E `capture-loop.spec.ts` |
| Artefact intake and local extraction | `backend/src/Taskdeck.Api/Controllers/ArtefactsController.cs`, `backend/src/Taskdeck.Application/Interfaces/IArtefactTextExtractor.cs`, `backend/src/Taskdeck.Application/Services/IArtefactExtractionService.cs` | `SourceArtefact` is the immutable user-owned source; extraction appends bounded, warning-bearing history and never mutates task state | `dotnet test backend/Taskdeck.sln -c Release -m:1 --filter "FullyQualifiedName~ArtefactExtraction"`; `MigrationBootstrapTests` |
| Context Fabric (durable capture, processing contracts) — ADR-0065 | `backend/src/Taskdeck.Domain/Entities/Capture.cs`, `Entities/SourceAsset.cs`, `Domain/Enums/CaptureTimeline.cs`, `Domain/Enums/CaptureSourceMapping.cs`, `Domain/Processing/ProcessingCapability.cs`, `Application/Services/CaptureIntakeService.cs`, `Application/Interfaces/ICaptureStore.cs` (+ `IRepresentationStore`, `IBlobStore` — contracts only), `Application/Processing/ProcessorManifest*.cs`, `Application/Processing/Protocol/WorkerProtocol.cs`, `Infrastructure/Repositories/EfCaptureStore.cs`; map `docs/architecture/CONTEXT_FABRIC.md`, protocol `docs/architecture/WORKER_PROTOCOL_V1.md` | A capture is valid the moment its source is stored and never becomes unreadable through job failure; `Capture.Id = LlmRequest.Id` (ID-preserving); capture state is three orthogonal axes (user disposition · processing summary · action state) and the timeline is a projection, never the only persisted truth; typed/pasted text is an immutable `SourceAsset`, never job state; every capture creation path writes through `CaptureIntakeService`; processors declare capabilities from `ProcessingCapability` and hold no mutation tools, and sidecars and remote processors may declare only externalizable capabilities (no `context.resolve` / `change.plan` / `change.verify`); Worker Protocol is v1-alpha until PdfPig and WhisperX both pass conformance; `IBlobStore` deletes only on the last released reference; `IRepresentationStore` is a draft with a nullable `CaptureId` only for the migration window; the `Captures`, `SourceAssets` and `SourceAssetTextPayloads` tables are written only when `ContextFabric:DualWriteCaptures` is on (default off) and Inbox reads stay on the queue row until CF-01 `#2255`; no new `CaptureSource` values and no new request-type lane predicates — dimensions come from `CaptureSourceMapping`; router v1 = constraints + ordered preference + route receipt, no scoring before the CF-24A `#2319` corpus | `CaptureTests`, `SourceAssetTests`, `CaptureTimelineTests`, `CaptureSourceMappingTests`, `ProcessingCapabilityTests` (Domain); `ProcessorManifestValidatorTests`, `WorkerProtocolSerializationTests`, `CaptureServiceDualWriteTests`, `LlmQueueServiceDualWriteTests` (Application); `MigrationBootstrapTests` (tables present + `EfCaptureStore` round-trip); tracker CF-00 `#2254` |
| Transcript persistence and evidence | `backend/src/Taskdeck.Domain/Entities/Transcript.cs`, `backend/src/Taskdeck.Application/Interfaces/ITranscriptRepository.cs`, `backend/src/Taskdeck.Infrastructure/Repositories/TranscriptRepository.cs`, migration `backend/src/Taskdeck.Infrastructure/Migrations/20260801173142_AddTranscripts.cs` | `Transcript.Text` is an independent normalized text record; the current capture path also persists transcript input in `LlmRequest.Payload` until `#1305` links triage to Transcript. Transcript reads/deletion/export are user-scoped; optional board/artefact deletion nulls references rather than deleting the transcript; `#1305` also owns evidence spans, provenance API, and Paper deep links | the three Transcript checkpoint commands in `docs/TESTING_GUIDE.md` |
| Proposal operation vocabulary | [`autodoc/interfaces/proposal-operation-vocabulary.md`](interfaces/proposal-operation-vocabulary.md), `ProposalOperationContractValidator`, `OperationHandlerRegistry`, `AutomationProposalService.GetProposalDiffAsync` | board-scoped preview/apply validation, card metadata handlers, chat executors, and `Taskdeck.Api/Mcp/WriteTools` | pipeline handler, proposal diff/revision, MCP/write-tool, and proposal API tests |
| Backend API/application | `backend/Taskdeck.sln`, `Api/`, `Application/`; DI at `Infrastructure/DependencyInjection.cs` | Domain has no infra/framework refs; Application no Api/Infra refs (Architecture.Tests); claims-first identity; stable HTTP 400/401/403/404/409; no cross-user leak | `dotnet test backend/Taskdeck.sln -c Release -m:1` (see `backend/CLAUDE.md`) |
| Frontend workspace | `frontend/taskdeck-web/src/`: `router`, `views`, `store/board*`, `composables/`, `api/http.ts`, `components/ui` (17 `Td*`) | Review-first UI gating; per-board SignalR (`useBoardRealtime.ts`), not global; `boardStore` is a facade over `store/board/*`; all HTTP through `api/http.ts` | `npm run typecheck`, `npm run build`, `npx vitest --run` (OOM-prone: `--maxWorkers=2`/targeted), Playwright (see `frontend/taskdeck-web/CLAUDE.md`) |
| Agent runtime & MCP | `Application` (`AutomationPolicyEngine`), **MCP surface in `Api`** (`Program.cs` `--mcp` branch, `Api/Mcp/*`), `.codex/config.toml`, `.mcp.json`, `docs/MCP_TOOLING_GUIDE.md` | Policy evaluated before execute; egress/telemetry guards; tool registry | security tests, MCP inventory/egress tests |
| Agent tooling / CI / docs | `.claude/`, `.codex/`, `scripts/github/Invoke-TaskdeckReadOnlyInventory.ps1`, `scripts/agent_hooks/` (manual ledger projection only), `.github/workflows/` (`ci-required.yml` = the required CI evidence; `smart-ci-shadow.yml` = the shadow planner + observation-mode gate), `ci/policy.v1.json` + `scripts/ci/smart-ci/` (planner, gate evaluator, estate measurement; map `docs/ci/SMART_CI.md`, decision ADR-0066, tracker CI-00 `#2324`), `scripts/check-*.mjs` | Delegated shell-backed inventory enters through the opt-in read-only argv wrapper; direct Git/GitHub mutation stays coordinator-owned; review and merge disposition come from live authority plus the canonical global pipeline; no Taskdeck-owned runtime hooks or local command-deny list; Smart CI is in **shadow mode** — the planner and gate change no job selection until the recall report (CI-02 `#2326`) and the gate is registered only by the maintainer (CI-03 `#2327`); CI-control paths (`.github/**`, `ci/**`, `scripts/ci/**`) are R4/T2 and qualify hosted-only, never on a self-hosted runner; the repository goes private for v0.3.0 by maintainer action only (CI-13 `#2337`) and no self-hosted runner is attached while it is public | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/github/Invoke-TaskdeckReadOnlyInventory.ps1 -SelfTest`; failure-ledger synchronization unittest, settings/tier parsing, worktree helper suite when touched, then docs gates (see `scripts/agent_hooks/CLAUDE.md`); `node --test scripts/ci/smart-ci/*.test.mjs` when `ci/**` or `scripts/ci/smart-ci/**` change |
| Docs & planning | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `docs/TESTING_GUIDE.md` | STATUS is source of truth for shipped reality; keep governance line intact | `node scripts/check-docs-governance.mjs`, `node scripts/check-golden-principles.mjs` |

## Interface-On-Top Convention

For any new/refactored complex domain: keep the public entry point obvious (route, controller,
service, facade); record invariants + edit seams + verify command in this file or
`autodoc/interfaces/<domain>.md`; point cross-domain imports at facades/application interfaces;
do not turn root docs into implementation references; update `docs/agentic/SKILL_REGISTRY.md`
only when workflow routing changes.

## Minimum Handoff Shape

```text
Changed: <files/seams>
Verified: <commands/results>
Not verified: <reason>
Failures/workarounds: <classification + future fix>
Docs/status sync: <updated or not needed>
Next safe slice: <one concrete action>
```
