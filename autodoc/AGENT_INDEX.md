# Agent Index - Taskdeck

Last reviewed: 2026-05-11.

This is a fast orientation layer for coding agents. It should point to interfaces and seams, not duplicate implementation details.

## Start Here

1. `docs/STATUS.md` - current shipped reality and constraints.
2. `AGENTS.md` - repo-wide operating contract.
3. `.codex/README.md` and `.codex/memories/00_ACTIVE.md` - Codex routing and active gate.
4. `.claude/README.md` and `CLAUDE.md` - Claude routing and compact contract.
5. `docs/IMPLEMENTATION_MASTERPLAN.md` - roadmap sequencing.
6. `docs/GOLDEN_PRINCIPLES.md` - stable invariants.
7. `docs/agentic/SKILL_REGISTRY.md` - skill selection.
8. `docs/agentic/AGENT_TOOL_PARITY.md` - Codex/Claude tool parity and native strengths.
9. Relevant `SKILL.md` under `.codex/skills/` or `.claude/skills/`.

## Do Not Read By Default

- `.claude/worktrees/`
- `.worktrees/`
- `frontend/taskdeck-web/node_modules/`
- build outputs, coverage outputs, Playwright traces, and generated artifacts
- `docs/archive/` unless active docs or the task explicitly point there
- large design/source packs under `docs/InReview/` or `docs/WIP/` unless reconciling them
- generated OpenAPI or compiled assets unless the task is about those artifacts

## Product And Engineering Seams

| Domain | Entry points | Meaty files | Verification hints |
| --- | --- | --- | --- |
| Capture to review to board | `backend/src/Taskdeck.Api/Controllers`, `frontend/taskdeck-web/src/views/InboxView.vue`, `ReviewView.vue` | capture stores, proposal services, automation executor, provenance services | capture/review unit tests, API integration tests, E2E capture loop |
| Proposal operation vocabulary | [`autodoc/interfaces/proposal-operation-vocabulary.md`](interfaces/proposal-operation-vocabulary.md), `OperationHandlerRegistry`, `AutomationProposalService.GetProposalDiffAsync` | card/board/column apply handlers, revision-aware preview, MCP/chat proposal executors | pipeline handler, proposal diff/revision, write-tool, and proposal API tests |
| Review-first AI roadmap | `taskdeck-12-week-roadmap-v4.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `backend/src/Taskdeck.Domain` | intent envelope, proposal provenance, confidence, egress, eval harness | roadmap invariant tests and targeted backend filters in `docs/TESTING_GUIDE.md` |
| Backend API/application slices | `backend/Taskdeck.sln`, `backend/src/Taskdeck.Api`, `backend/src/Taskdeck.Application` | repositories, services, controllers, migrations | `dotnet test backend/Taskdeck.sln -c Release -m:1` or narrow filters |
| Frontend workspace | `frontend/taskdeck-web/src/router`, `views`, `store`, `components/ui` | route views, Pinia stores, composables, Td primitives | `npm run typecheck`, `npm run build`, `npx vitest --run`, targeted Playwright |
| Agent runtime and MCP | `backend/src/Taskdeck.Application`, `docs/MCP_TOOLING_GUIDE.md`, `docs/agentic/AGENT_TOOL_PARITY.md`, `.codex/config.toml`, `.mcp.json` | policy evaluator, tool registry, egress/telemetry guards | security tests, MCP inventory tests, egress/telemetry tests |
| Agent operating layer | `AGENTS.md`, `CLAUDE.md`, `.codex/README.md`, `.claude/README.md` | `.codex/skills/*`, `.claude/skills/*`, `docs/agentic/*`, `scripts/agent_hooks/*` | skill validation, docs governance, hook smoke tests |
| Docs and planning | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `docs/TESTING_GUIDE.md` | topical docs under `docs/` | `node scripts/check-docs-governance.mjs`, `node scripts/check-golden-principles.mjs` |

## Interface-On-Top Convention

For any new or refactored complex domain:

1. Keep the public entry point obvious: route, controller, service, facade, index, or README agent map.
2. Record invariants, edit seams, and verification commands in `autodoc/AGENT_INDEX.md` or `autodoc/interfaces/<domain>.md`.
3. Keep cross-domain imports pointed at facades or application interfaces where the architecture already provides them.
4. Do not turn root docs into implementation references; link to the domain map or topical doc.
5. Update `docs/agentic/SKILL_REGISTRY.md` only when workflow routing changes.

## Minimum Handoff Shape

```text
Changed: <files/seams>
Verified: <commands/results>
Not verified: <reason>
Failures/workarounds: <classification + future fix>
Docs/status sync: <updated or not needed>
Next safe slice: <one concrete action>
```
