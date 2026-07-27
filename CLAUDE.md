# CLAUDE.md — Taskdeck

Global laws (review, tiers, worktrees, questions, model routing) live in `~/.claude/CLAUDE.md` and
auto-load. This file carries only what is true of **this** repo. Do not restate global doctrine here.

## What Taskdeck is

Local-first execution workspace for developers: transcripts/notes in → evidence-linked **proposals**
→ human-approved board changes. No silent or destructive mutations; SQLite persistence.
.NET 8 backend (clean architecture) + Vue 3/Vite frontend + a write-gated MCP server.

**Active direction:** ADR-0044 revival (2026-07-10) — free open beta, spine is `docs/REVIVAL_PLAN.md`;
work not on its ratified wave list is not taken. This supersedes the 2026-06-13 archive pivot and the
archive tracker #1278 (still open, seeded 2026-07-02, kept as the checkpoint fallback).

## Orient (do NOT bulk-read the big docs)

1. `autodoc/AGENT_INDEX.md` — the seam map. Start here, find your region, jump to entry points.
2. `docs/STATUS.md` (~1.5k lines) — shipped reality, **section-read only**. Precedence: STATUS > AGENTS.md > this file.
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
| `scripts/agent_hooks/**` (deny floor) | `py -3 -B scripts/agent_hooks/smoke_test.py` | ~10s, green |
| One backend layer | `dotnet test backend/tests/Taskdeck.<Layer>.Tests/Taskdeck.<Layer>.Tests.csproj -c Release -m:1` | Domain: ~30s cold, 1636 passed |
| Backend, cross-layer | `dotnet test backend/Taskdeck.sln -c Release -m:1` | minutes — last resort |
| Backend, one class | add `--filter "FullyQualifiedName~MyTestClass"` | — |
| One frontend spec | `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <path/to.spec.ts>` | ~5s |
| Frontend, broad | `npm run typecheck; npm run build; npx vitest --run --maxWorkers=2` | slow; bare `vitest --run` **OOMs on this box** |
| E2E | `cd frontend/taskdeck-web; npx playwright test tests/e2e/<file>.spec.ts --reporter=line` | needs a running stack |

`ci-required.yml` is the sole merge gate. PRs touching `.github/workflows/`, `deploy/`, `scripts/`,
or `*.csproj` also trigger CI Extended — it must be green before merging those.

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
is a deterministic regex extractor, never the LLM. Realtime is **per-board** SignalR, not global.
LLM providers: mock by default; OpenAI/Gemini behind config gates (`docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`).

## Repo-specific pitfalls

- **Long paths.** A `git worktree add` into a deep directory fails with `Filename too long` —
  `docs/InReview/MVP_EXPANSION/EXPANDED/...` is ~115 chars on its own. Keep worktree roots short.
- **DCO is enforced.** Every commit needs `Signed-off-by:`. Use `git commit -s --no-gpg-sign`,
  `git merge --signoff --no-gpg-sign <branch>`, `git commit -s --no-gpg-sign --no-edit` after resolving
  conflicts. Never `--no-verify`. GitHub's server-side merge commit is outside the PR commit set — do not
  rewrite shared history to add a trailer to it.
- **Deny floor is stricter than T3 by design.** `reset --hard`, `clean -f`, `checkout --` are hard-denied
  by `.claude/settings.json` + `scripts/agent_hooks/pre_tool_use.py` after the 2026-05/06 main-leak
  incidents. A deny is final. Deny-floor changes are T4-class.
- **PowerShell:** no `&&` chaining; use `;` and check `$LASTEXITCODE`.
- **Git resolution:** if `git` resolves to Cygwin or throws signal errors, run
  `bash scripts/check-git-env.sh` (or `powershell -File scripts/check-git-env.ps1`); it also clears a
  stale `.git/index.lock` after confirming no git process is live.
- **`.worktrees/` holds ~30 stale issue checkouts** with unpushed branches. Do not prune or clean them.
- Create issue worktrees with `scripts/git/New-CodexIssueWorktree.ps1`; first command inside one is
  `powershell -File scripts/worktree_guard.ps1`.

## Definition of done

Behavior changes ship with tests. Errors handled explicitly, never swallowed. Stable HTTP codes
(400/401/403/404/409), claims-first identity, no cross-user leaks, never trust client input for identity.
Update `docs/STATUS.md` when shipped reality changes and `docs/IMPLEMENTATION_MASTERPLAN.md` for roadmap
impact. Backend: C# conventions, 4-space, layer purity. Frontend: `PascalCase.vue`, `<script setup>`.

**ADRs** live in `docs/decisions/` (template + `INDEX.md` there). Write one when a change picks between
competing approaches, sets a project-wide constraint, is hard to reverse, or would surprise a future
contributor — technology, data model, security posture, automation safety boundary, strategy.

## Authority

T3 workshop per `.agent-harness/tier.json` — push free, merge free once `ci-required` is green at the head
and the global law-2 gate is satisfied. Human-action file: `OUTSTANDING_TASKS.md`.

## Key docs

`docs/REVIVAL_PLAN.md` (active spine) · `docs/STATUS.md` · `docs/IMPLEMENTATION_MASTERPLAN.md` ·
`docs/GOLDEN_PRINCIPLES.md` · `docs/TESTING_GUIDE.md` · `docs/ISSUE_EXECUTION_GUIDE.md` ·
`docs/MCP_TOOLING_GUIDE.md` · `docs/decisions/INDEX.md` · `docs/agentic/` (question, failure-ledger,
guide-update protocols) · `docs/platform/CONFIGURATION_REFERENCE.md` · `docs/platform/EF_MIGRATION_WORKFLOW.md` ·
`AGENTS.md` (Codex-facing contributor protocol).
