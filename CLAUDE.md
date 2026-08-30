# CLAUDE.md — Taskdeck

Global laws (review, tiers, worktrees, questions, model routing) live in `~/.claude/CLAUDE.md` and
auto-load. This file carries only what is true of **this** repo. Do not restate global doctrine here.

## What Taskdeck is

Local-first execution workspace for developers: transcripts/notes in → evidence-linked **proposals**
→ human-approved board changes. No silent or destructive mutations; SQLite persistence.
.NET 8 backend (clean architecture) + Vue 3/Vite frontend + a write-gated MCP server.

**Active direction:** strategy spine is `docs/strategy/PRODUCT_DIRECTION.md` (2026-08-23 — adaptive
work OS destination, context-to-action engine, transcripts/notes/captures wedge); execution plan is
`docs/REVIVAL_PLAN.md` (ADR-0044 revival, free open beta). ADR-0051 adds a bounded
autonomous-admission lane for acceptance-ready tracked backlog while keeping new product surfaces
inside the plan/ADR boundary. The 2026-06-13 archive pivot is superseded (archive remains only the
checkpoint fallback). Shipped trust model stays review-first (ADR-0003/GP-06/ADR-0056); the
delegated-autonomy future is ADR-0057, **Accepted as direction only (2026-08-24, openness
caveat) — no implementation is in force or buildable without its own separate gate**. The repository
**goes private for the v0.3.0 release** on the maintainer's personal GitHub Pro account (directive
2026-08-30; ADR-0066 Smart CI Fabric, tracker CI-00 `#2324`): CI-control changes (`.github/**`, `ci/**`,
`scripts/ci/**`) are R4 and qualify hosted-only; the Smart CI lane is shadow-only until the maintainer
registers `Smart CI / Required Gate` — `docs/ci/SMART_CI.md`.

## Orient (do NOT bulk-read the big docs)

1. `autodoc/AGENT_INDEX.md` — the seam map. Start here, find your region, jump to entry points.
2. `docs/STATUS.md` (~775 lines after the 2026-08-23 head-lean rotation) — shipped reality, **section-read only**. Precedence: STATUS > AGENTS.md > this file.
3. `OUTSTANDING_TASKS.md` — the human-action file (global law 5). Surface open `[ ]` items in every summary.
4. Region rules auto-load when you touch files: `backend/CLAUDE.md`, `frontend/taskdeck-web/CLAUDE.md`,
   `scripts/agent_hooks/CLAUDE.md`. Pick a workflow skill from `.claude/skills/README.md` (local skills beat plugins).

## Proving checks (narrowest command per seam)

Run only what your change touches. Timings measured 2026-07-27 on this box (warm caches).

| Changed seam | Command (repo root unless noted) | Measured |
| --- | --- | --- |
| Root/agent docs, `docs/**` | `node scripts/check-docs-governance.mjs` | ~1s, green |
| `docs/GOLDEN_PRINCIPLES.md`, invariants | `node scripts/check-golden-principles.mjs` | ~1s, green |
| `.github/ISSUE_TEMPLATE/**`, `AGENTS.md` project ops | `node scripts/check-github-ops-governance.mjs` | ~1s, green |
| `ci/**`, `scripts/ci/smart-ci/**`, `.github/workflows/smart-ci-shadow.yml` | `node --test scripts/ci/smart-ci/*.test.mjs` | ~1s, green |
| Failure-ledger projection | `py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py"` | ~1s, 11 passed |
| One backend layer | `dotnet test backend/tests/Taskdeck.<Layer>.Tests/Taskdeck.<Layer>.Tests.csproj -c Release -m:1` | Domain: ~30s cold, 1636 passed |
| Backend, cross-layer | `dotnet test backend/Taskdeck.sln -c Release -m:1` | minutes — last resort |
| Backend, one class | add `--filter "FullyQualifiedName~MyTestClass"` | — |
| One frontend spec | `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <path/to.spec.ts>` | ~5s |
| Frontend, broad | `cd frontend/taskdeck-web; npm run typecheck; npm run build; npx vitest --run --maxWorkers=2` | slow; bare `vitest --run` **OOMs on this box** |
| E2E | `cd frontend/taskdeck-web; npx playwright test tests/e2e/<file>.spec.ts --reporter=line` | needs a running stack |

`ci-required.yml` is the required CI gate. PRs touching `.github/workflows/`, `backend/`, `frontend/`,
`deploy/`, `scripts/`, or `*.csproj` also trigger CI Extended — an optional, non-blocking lane (several
jobs are label-gated). Read its results, but it does not gate the merge. `smart-ci-shadow.yml` (ADR-0066) is observation-only: red
means a planner/schema defect, never a product verdict, until CI-03 registers the gate.

## Run it

```bash
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj   # API :5000, Swagger /swagger
cd frontend/taskdeck-web && npm install && npm run dev              # :5173, Node 24.x
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

## Architecture

**Backend** `backend/src/` — `Domain` (pure, no infra refs) → `Application` (use cases, no Api/Infra refs)
→ `Infrastructure` (EF Core + SQLite, adapters) → `Api` (endpoints, auth, SignalR, `--mcp` branch in
`Program.cs`, `Api/Mcp/*`); `Cli` is a separate entry point. `Architecture.Tests` enforces the boundaries.
Tests mirror the layout in `backend/tests/`.

**Frontend** `frontend/taskdeck-web/src/` — `views/` (route pages; large ones are thin shells <300 lines
delegating to composables/components), `store/` (Pinia; `boardStore` is a facade over `store/board/*`),
`api/` (all HTTP through `api/http.ts`), `composables/`, `components/ui/` (17 shared `Td*` primitives),
`router/`. Tailwind + TS + `<script setup>`.

**Flow:** capture → captureStore → inbox API → proposal generated → ReviewView → explicit approve, then
explicit execute (needs an Idempotency-Key). Preview == Apply (both materialize the latest
`ProposalRevision`). Provenance is server-stamped; client identity fields are rejected. Triage extraction
is LLM-backed for transcript-source captures (`LlmCaptureTriageExtractor` — kill switch → provider health
→ quota → completion → usage recording, every failure returned as an outcome, never thrown) and degrades
to the deterministic extractor otherwise. Realtime is **per-board** SignalR, not global.
LLM providers: mock by default; OpenAI behind config gates, default model `gpt-5.6-luna`.
Retired Gemini selectors/settings fail startup with migration guidance — `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`.

## Repo-specific pitfalls

- **Long paths.** A `git worktree add` into a deep directory fails with `Filename too long` —
  `docs/InReview/MVP_EXPANSION/EXPANDED/...` is ~115 chars on its own. Keep worktree roots short.
- **DCO enforcement is paused.** By explicit maintainer decision on 2026-08-23, `Signed-off-by:`
  trailers are optional and do not affect merge eligibility. Do not rewrite commits or add trailers to
  another contributor's work. The dormant verifier assets remain under `scripts/ci/`; `#2019` tracks a
  possible future restoration and does not itself authorize reactivation. Never use `--no-verify`.
- **No Taskdeck-owned runtime hooks.** `.claude/settings.json` has no hook groups or local command-deny
  list, and the root has no `.codex/hooks.json`. Declared authority, global laws, CI, and worktree guards still
  apply; user-, organization-, and runtime-level hooks are separate effective layers.
- **PowerShell:** no `&&` chaining; use `;` and check `$LASTEXITCODE`.
- **Git resolution:** if `git` resolves to Cygwin or throws signal errors, run
  `bash scripts/check-git-env.sh` (or `powershell -File scripts/check-git-env.ps1`); it also clears a
  stale `.git/index.lock` after confirming no git process is live.
- **`.worktrees/` holds ~30 stale issue checkouts** with unpushed branches. Do not prune or clean them.
- Create issue worktrees with `scripts/git/New-CodexIssueWorktree.ps1`. Run its complete printed handoff:
  the exact pinned-Git `worktree_guard.ps1` command first, then the bounded
  `Initialize-CodexIssueWorktree.ps1` command before creating the issue branch.

## Definition of done

Behavior changes ship with tests. Errors handled explicitly, never swallowed. Stable HTTP codes
(400/401/403/404/409), claims-first identity, no cross-user leaks, never trust client input for identity.
Update `docs/STATUS.md` when shipped reality changes and `docs/IMPLEMENTATION_MASTERPLAN.md` for roadmap
impact. Backend: C# conventions, 4-space, layer purity. Frontend: `PascalCase.vue`, `<script setup>`.

**ADRs** live in `docs/decisions/` (template + `INDEX.md` there). Write one when a change picks between
competing approaches, sets a project-wide constraint, is hard to reverse, or would surprise a future
contributor — technology, data model, security posture, automation safety boundary, strategy.

## Authority

Read `.agent-harness/tier.json` live for tier and push/merge authority; do not mirror those values here.
`ci-required` is Taskdeck's repository evidence gate, while review/merge disposition comes from the
canonical global laws and `review-and-ship` pipeline. Human-action file: `OUTSTANDING_TASKS.md`.

## Key docs

`docs/strategy/PRODUCT_DIRECTION.md` (strategy spine) · `docs/REVIVAL_PLAN.md` (execution plan) ·
`docs/STATUS.md` · `docs/IMPLEMENTATION_MASTERPLAN.md` ·
`docs/GOLDEN_PRINCIPLES.md` · `docs/TESTING_GUIDE.md` · `docs/ISSUE_EXECUTION_GUIDE.md` ·
`docs/MCP_TOOLING_GUIDE.md` · `docs/decisions/INDEX.md` · `docs/agentic/` (question, failure-ledger,
guide-update protocols) · `docs/platform/CONFIGURATION_REFERENCE.md` · `docs/platform/EF_MIGRATION_WORKFLOW.md` ·
`AGENTS.md` (Codex-facing contributor protocol).
