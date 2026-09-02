# CLAUDE.md — Taskdeck

Global laws (review, tiers, worktrees, questions, model routing) live in `~/.claude/CLAUDE.md` and
auto-load. This file carries only what is true of **this** repo. Do not restate global doctrine here.

## What Taskdeck is

Local-first execution workspace for developers: transcripts/notes in → evidence-linked **proposals**
→ human-approved board changes. No silent or destructive mutations; SQLite persistence.
.NET 8 backend (clean architecture) + Vue 3/Vite frontend + a write-gated MCP server.

**Direction:** strategy spine `docs/strategy/PRODUCT_DIRECTION.md`; execution plan `docs/REVIVAL_PLAN.md`
(ADR-0044 revival, ADR-0051 bounded autonomous admission). The shipped trust model is review-first
(ADR-0003/GP-06/ADR-0056); ADR-0057 delegated autonomy is **direction only — nothing under it is buildable
without its own separate gate**. The repository goes private for the v0.3.0 release (directive 2026-08-30).

## Orient (do NOT bulk-read the big docs)

1. `autodoc/AGENT_INDEX.md` — the seam map. Start here, find your region, jump to entry points.
2. `docs/STATUS.md` — shipped reality, **section-read only**. Precedence: STATUS > AGENTS.md > this file.
3. `OUTSTANDING_TASKS.md` — the human-action file (global law 5). Surface open `[ ]` items in every summary.
4. Region rules auto-load by path: `backend/CLAUDE.md`, `frontend/taskdeck-web/CLAUDE.md`,
   `scripts/agent_hooks/CLAUDE.md`, `.claude/rules/ci-control.md` (`.github/**`, `ci/**`, `scripts/ci/**`),
   `.claude/rules/docs.md` (`docs/**`, root `*.md`). Workflow skills: `.claude/skills/README.md` — local
   skills beat plugins; they trigger by description.

## Claude Code runtime facts

- Auto mode is the user-level default. `bypassPermissions` comes only from user settings, managed policy, or
  a launch flag — project files cannot grant it (2.1.257). The committed project default is `acceptEdits`.
- The global floor hook runs on every Bash call in every mode. It refuses `$var`-built command names and
  paths, and it has refused heredocs whose body contained backticks ("dynamic redirect target cannot be
  inspected") — write literal commands, `Write` multi-line edits to a scratchpad script and run it, and pass
  PR bodies with `--body-file`.
- `AGENTS.md` is Codex-facing and is **not** auto-loaded by Claude; read it only for Codex coordination.
- Permission rules are prefix rules: `Bash(gh:*)`, never `Bash(gh :*)` — `:*` is a wildcard only at the end.

## Proving checks (narrowest command per seam)

Run only what your change touches. Everything is seconds unless marked.

| Changed seam | Command (repo root unless noted) |
| --- | --- |
| Root/agent docs, `docs/**` | `node scripts/check-docs-governance.mjs` |
| `docs/GOLDEN_PRINCIPLES.md`, invariants | `node scripts/check-golden-principles.mjs` |
| `.github/ISSUE_TEMPLATE/**`, `AGENTS.md` project ops | `node scripts/check-github-ops-governance.mjs` |
| `ci/**`, `scripts/ci/smart-ci/**`, the smart-ci shadow workflow | `node --test scripts/ci/smart-ci/*.test.mjs` |
| Failure-ledger projection | `py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py"` |
| `scripts/analysis/**` refactoring ranker | `py -3 -B -m unittest discover -s scripts/analysis -p "test_rank_refactor_candidates.py"` |
| `scripts/agentic/**`, the orchestrator guard recipe | `powershell -File scripts/agentic/Test-Assert-TaskdeckCheckoutFingerprint.ps1` |
| One backend test project | `dotnet test backend/tests/<Project>/<Project>.csproj -c Release -m:1` — `Taskdeck.{Domain,Application,Api,Cli,Architecture,Integration}.Tests` (~30 s cold; Infrastructure is covered by Integration + Api) |
| Backend, one class | add `--filter "FullyQualifiedName~MyTestClass"` |
| Backend, before the PR | `dotnet test backend/Taskdeck.sln -c Release -m:1` (minutes; the `backend/AGENTS.md` required check — once per backend PR, CI repeats it) |
| One frontend spec | `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <path/to.spec.ts>` |
| Frontend, broad | `cd frontend/taskdeck-web; npm run typecheck; npm run build; npx vitest --run --maxWorkers=2` (slow; bare `vitest --run` **OOMs on this box**) |
| E2E | `cd frontend/taskdeck-web; npx playwright test tests/e2e/<file>.spec.ts --reporter=line` (needs a running stack) |

`ci-required.yml` is the required CI gate; CI Extended and the Smart CI shadow lane are advisory. The CI
region's rules (R4 class, what "red" means there) live in `.claude/rules/ci-control.md`.

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

**Frontend** `frontend/taskdeck-web/src/` — `views/` (route pages; large ones are thin shells delegating to
composables/components), `store/` (Pinia; `boardStore` is a facade over `store/board/*`), `api/` (all HTTP
through `api/http.ts`), `composables/`, `components/ui/` (shared `Td*` primitives), `router/`.
Tailwind + TS + `<script setup>`.

**Flow:** capture → captureStore → inbox API → proposal generated → ReviewView → explicit approve, then
explicit execute (needs an Idempotency-Key). Preview == Apply (both materialize the latest
`ProposalRevision`). Provenance is server-stamped; client identity fields are rejected. Triage extraction
is LLM-backed for transcript-source captures (`LlmCaptureTriageExtractor` — kill switch → provider health
→ quota → completion → usage recording, every failure returned as an outcome, never thrown) and degrades
to the deterministic extractor otherwise. Realtime is **per-board** SignalR, not global.
LLM providers: mock by default; OpenAI behind config gates, default model `gpt-5.6-luna`.
Retired Gemini selectors/settings fail startup with migration guidance — `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`.

## Repo-specific pitfalls

- **Long paths.** A `git worktree add` into a deep directory fails with `Filename too long`. Keep worktree
  roots short (`C:/Users/jekyt/source/td-<slug>` works).
- **DCO enforcement is paused** (maintainer decision 2026-08-23): `Signed-off-by:` trailers are optional
  and do not affect merge eligibility. Never rewrite another contributor's commits to add one; never use
  `--no-verify`. `#2019` tracks a possible restoration and does not itself authorize it.
- **No Taskdeck-owned runtime hooks.** `.claude/settings.json` has no hook groups or command-deny list and
  the root has no `.codex/hooks.json`. User-level hooks (the floor) still apply.
- **PowerShell:** no `&&` chaining; use `;` and check `$LASTEXITCODE`.
- **Git resolution:** if `git` resolves to Cygwin or throws signal errors, run `bash scripts/check-git-env.sh`
  (or `powershell -File scripts/check-git-env.ps1`); it also clears a stale `.git/index.lock` safely.
- **`.worktrees/` holds ~70 stale Codex issue checkouts** with unpushed branches. Do not prune or clean them.
- Create issue worktrees with `scripts/git/New-CodexIssueWorktree.ps1` and run its complete printed
  handoff: the exact pinned-Git `worktree_guard.ps1` command first, then the bounded
  `Initialize-CodexIssueWorktree.ps1` command — contract in `docs/WORKTREE_AGENT_PROTOCOL.md`.

## Definition of done

Behavior changes ship with tests. Errors handled explicitly, never swallowed. Stable HTTP codes
(400/401/403/404/409), claims-first identity, no cross-user leaks, never trust client input for identity.
Backend: C# conventions, 4-space, layer purity. Frontend: `PascalCase.vue`, `<script setup>`.
Shipped reality changed → update `docs/STATUS.md`; sequencing or delivery history changed →
`docs/IMPLEMENTATION_MASTERPLAN.md`; a choice between competing approaches, a project-wide constraint, or a
hard-to-reverse change → an ADR in `docs/decisions/`. This applies to code-only PRs too — the mechanics live in
`.claude/rules/docs.md`, which loads only when a doc is touched.

## Authority

Read `.agent-harness/tier.json` live for tier and push/merge authority; do not mirror those values here.
`ci-required` is Taskdeck's repository evidence gate, while review/merge disposition comes from the
canonical global laws and `review-and-ship` pipeline. Human-action file: `OUTSTANDING_TASKS.md`.

## Key docs

`docs/strategy/PRODUCT_DIRECTION.md` (strategy spine) · `docs/REVIVAL_PLAN.md` (execution plan) ·
`docs/STATUS.md` · `docs/IMPLEMENTATION_MASTERPLAN.md` · `docs/GOLDEN_PRINCIPLES.md` ·
`docs/TESTING_GUIDE.md` · `docs/ISSUE_EXECUTION_GUIDE.md` · `docs/MCP_TOOLING_GUIDE.md` ·
`docs/decisions/INDEX.md` · `docs/agentic/` (question, failure-ledger, guide-update protocols) ·
`docs/platform/CONFIGURATION_REFERENCE.md` · `docs/platform/EF_MIGRATION_WORKFLOW.md` ·
`AGENTS.md` (Codex-facing contributor protocol).
