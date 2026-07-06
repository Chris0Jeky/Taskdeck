# Agent Index - Taskdeck (seam map)

Last-Verified: 2026-07-06 (4-region code exploration). Stamp policy: re-verify when a seam
moves; the budget script flags this map when the stamp is >90 days old.

This is the repo's seam map — a fast orientation layer for coding agents. It points to
interfaces, invariants, and verification commands; it does not duplicate implementation.
It is the Taskdeck equivalent of the harness `AGENT_MAP.md` (grandfathered name).

## Orient (do NOT bulk-read the big docs)

- **Start here** = this file. Find your region in the seams table, jump to its entry points.
- Region rules auto-load: each major directory has a scoped `CLAUDE.md` (`backend/`,
  `frontend/taskdeck-web/`, `scripts/agent_hooks/`) — Claude Code loads it when you touch files
  there. Read that, not the whole repo.
- Current shipped state: `docs/STATUS.md` (source of truth) — read the relevant section, it is
  ~1.3k lines; do not read it end-to-end. Roadmap: `docs/IMPLEMENTATION_MASTERPLAN.md` (same).
- Contract: `AGENTS.md`. Invariants: `docs/GOLDEN_PRINCIPLES.md`. Skills: `.claude/skills/`.

## Do Not Read By Default

- `.claude/worktrees/`, `.worktrees/`, `frontend/taskdeck-web/node_modules/`
- build/coverage/dist outputs, Playwright traces, generated OpenAPI/compiled assets
- EF `**/Migrations/*.Designer.cs` snapshots (unless a migration is the task)
- `docs/agentic/failure_ledger.jsonl` (read the rendered `FAILURE_LEDGER.md` instead)
- `docs/archive/` and large packs under `docs/InReview/` / `docs/WIP/` unless reconciling them
- `C:/Users/jekyt/source/agent-harness` — sibling checkout (the blueprint), outside this repo

## Product And Engineering Seams

| Domain | Entry points | Invariants (load-bearing) | Verify |
| --- | --- | --- | --- |
| Capture → review → board | `Api/Controllers/CaptureController.cs`, `AutomationProposalsController.cs`; `views/InboxView.vue`, `ReviewView.vue`, `composables/useReviewProposals.ts` | **Preview == Apply** (#1235: diff + executor both materialize the latest `ProposalRevision`); approve & execute are two explicit calls; execute needs an Idempotency-Key; provenance server-stamped, client identity fields rejected; triage is a deterministic regex extractor (`deterministic-extractor`), never the LLM | capture/review unit + `CaptureApiTests`, `ProposalRevisionApiTests`; E2E `capture-loop.spec.ts` |
| Backend API/application | `backend/Taskdeck.sln`, `Api/`, `Application/`; DI at `Infrastructure/DependencyInjection.cs` | Domain has no infra/framework refs; Application no Api/Infra refs (Architecture.Tests); claims-first identity; stable HTTP 400/401/403/404/409; no cross-user leak | `dotnet test backend/Taskdeck.sln -c Release -m:1` (see `backend/CLAUDE.md`) |
| Frontend workspace | `frontend/taskdeck-web/src/` router, views, `store/board*`, `composables/`, `api/http.ts`, `components/ui` (17 `Td*`) | Review-first UI gating; per-board SignalR (`useBoardRealtime.ts`), not global; `boardStore` is a facade over `store/board/*`; all HTTP through `api/http.ts` | `npm run typecheck`, `npm run build`, `npx vitest --run` (OOM-prone: `--maxWorkers=2`/targeted), Playwright (see `frontend/taskdeck-web/CLAUDE.md`) |
| Agent runtime & MCP | `Application` (`AutomationPolicyEngine`), **MCP surface in `Api`** (`Program.cs` `--mcp` branch, `Api/Mcp/*`), `.codex/config.toml`, `.mcp.json`, `docs/MCP_TOOLING_GUIDE.md` | Policy evaluated before execute; egress/telemetry guards; tool registry | security tests, MCP inventory/egress tests |
| Harness / CI / docs | `.claude/`, `scripts/agent_hooks/` (deny floor + ledger), `.github/workflows/` (`ci-required.yml` = the gate), `scripts/check-*.mjs` | `ci-required` is the sole merge gate; docs-governance `Last Updated: YYYY-MM-DD` exact line; hook `smoke_test.py` green; deny-floor changes are T4-class | `python scripts/agent_hooks/smoke_test.py`; `node scripts/check-docs-governance.mjs` (see `scripts/agent_hooks/CLAUDE.md`) |
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
