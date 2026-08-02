# Agent Index - Taskdeck (seam map)

Last-Verified: 2026-08-02 (routing plus transcript/MCP/container and map-reduce seams; other regions retain the
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
| Capture → review → board | `Api/Controllers/CaptureController.cs`, `AutomationProposalsController.cs`; `Application/Services/LlmCaptureTriageExtractor.cs` and `TranscriptTriageChunking.cs`; `views/InboxView.vue`, `ReviewView.vue`, `composables/useReviewProposals.ts` | **Preview == Apply** (#1235: diff + executor both materialize the latest `ProposalRevision`); approve & execute are two explicit calls; execute needs an Idempotency-Key; provenance server-stamped, client identity fields rejected; transcript triage uses the LLM-backed extractor only after kill-switch/provider-health/quota gates and otherwise records deterministic fallback honestly. Under-budget transcripts stay one provider call; only a budget-forced split maps chunks, and any failed map leg falls back for the complete capture. | `TranscriptTriageChunkingTests`, `LlmCaptureTriageExtractorTests`, `TranscriptTriageLlmGoldenPathIntegrationTests`; capture/review unit + `CaptureApiTests`, `ProposalRevisionApiTests`; E2E `capture-loop.spec.ts` |
| Artefact intake and local extraction | `backend/src/Taskdeck.Api/Controllers/ArtefactsController.cs`, `backend/src/Taskdeck.Application/Interfaces/IArtefactTextExtractor.cs`, `backend/src/Taskdeck.Application/Services/IArtefactExtractionService.cs` | `SourceArtefact` is the immutable user-owned source; extraction appends bounded, warning-bearing history and never mutates task state | `dotnet test backend/Taskdeck.sln -c Release -m:1 --filter "FullyQualifiedName~ArtefactExtraction"`; `MigrationBootstrapTests` |
| Transcript persistence and evidence | `backend/src/Taskdeck.Domain/Entities/Transcript.cs`, `backend/src/Taskdeck.Application/Interfaces/ITranscriptRepository.cs`, `backend/src/Taskdeck.Infrastructure/Repositories/TranscriptRepository.cs`, migration `backend/src/Taskdeck.Infrastructure/Migrations/20260801173142_AddTranscripts.cs` | `Transcript.Text` is an independent normalized text record; the current capture path also persists transcript input in `LlmRequest.Payload` until `#1305` links triage to Transcript. Transcript reads/deletion/export are user-scoped; optional board/artefact deletion nulls references rather than deleting the transcript; `#1305` also owns evidence spans, provenance API, and Paper deep links | the three Transcript checkpoint commands in `docs/TESTING_GUIDE.md` |
| Proposal operation vocabulary | [`autodoc/interfaces/proposal-operation-vocabulary.md`](interfaces/proposal-operation-vocabulary.md), `ProposalOperationContractValidator`, `OperationHandlerRegistry`, `AutomationProposalService.GetProposalDiffAsync` | board-scoped preview/apply validation, card metadata handlers, chat executors, and `Taskdeck.Api/Mcp/WriteTools` | pipeline handler, proposal diff/revision, MCP/write-tool, and proposal API tests |
| Backend API/application | `backend/Taskdeck.sln`, `Api/`, `Application/`; DI at `Infrastructure/DependencyInjection.cs` | Domain has no infra/framework refs; Application no Api/Infra refs (Architecture.Tests); claims-first identity; stable HTTP 400/401/403/404/409; no cross-user leak | `dotnet test backend/Taskdeck.sln -c Release -m:1` (see `backend/CLAUDE.md`) |
| Frontend workspace | `frontend/taskdeck-web/src/`: `router`, `views`, `store/board*`, `composables/`, `api/http.ts`, `components/ui` (17 `Td*`) | Review-first UI gating; per-board SignalR (`useBoardRealtime.ts`), not global; `boardStore` is a facade over `store/board/*`; all HTTP through `api/http.ts` | `npm run typecheck`, `npm run build`, `npx vitest --run` (OOM-prone: `--maxWorkers=2`/targeted), Playwright (see `frontend/taskdeck-web/CLAUDE.md`) |
| Agent runtime & MCP | `Application` (`AutomationPolicyEngine`), **MCP surface in `Api`** (`Program.cs` `--mcp` branch, `Api/Mcp/*`), `.codex/config.toml`, `.mcp.json`, `docs/MCP_TOOLING_GUIDE.md` | Policy evaluated before execute; egress/telemetry guards; tool registry | security tests, MCP inventory/egress tests |
| Agent tooling / CI / docs | `.claude/`, `.codex/`, `scripts/agent_hooks/` (manual ledger projection only), `.github/workflows/` (`ci-required.yml` = the required CI evidence), `scripts/check-*.mjs` | Review and merge disposition come from the live authority declaration plus the canonical global pipeline; docs-governance `Last Updated: YYYY-MM-DD` exact line; no Taskdeck-owned runtime hooks or local command-deny list | Failure-ledger synchronization unittest, settings/tier parsing, worktree helper suite when touched, then docs gates (see `scripts/agent_hooks/CLAUDE.md`) |
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
