# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Is Taskdeck

A local-first execution workspace for developers. Core thesis: near-zero-friction capture with review-first (proposal-based) automation -- no silent or destructive mutations. Local persistence via SQLite.

**Direction (2026-07-10, ADR-0044):** revival — ship a **free open beta** (adoption/feedback/exposure; commercial side developed in parallel), positioned as the local-first, review-first **action-item engine** (transcripts in → evidence-linked proposals → human-approved board changes) with the write-gated MCP server as the developer second act. The active planning spine is `docs/REVIVAL_PLAN.md`; work not on its ratified wave list is not taken. This supersedes the 2026-06-13 archive pivot (retained as the checkpoint fallback).

## Outstanding Tasks (read first, surface always)

`OUTSTANDING_TASKS.md` (repo root) is the maintainer's durable cross-session checklist.

- **Read it at the start of every session.**
- **Surface its open (`[ ]`) items — with their IDs — whenever you give a summary, status update, handoff, or "what's next."** The maintainer relies on this so nothing is forgotten across context resets.
- **Only check off / remove a task when the maintainer explicitly says it is done** (then mark it and add a dated changelog line). Never auto-complete an item because a related PR was opened.
- Add new outstanding tasks there when asked to remember something, or when substantial work is deferred.

## Required Reading Before Changes

1. `docs/STATUS.md` -- source of truth for current shipped state (always read first)
2. `docs/IMPLEMENTATION_MASTERPLAN.md` -- delivery history, planned work, roadmap sequencing, and strategic intentions
3. `docs/GOLDEN_PRINCIPLES.md` -- stable invariants and guardrails
4. `AGENTS.md` -- full contributor protocol, definition of done, output expectations
5. `.claude/README.md` -- Claude Code workspace routing, local skills, and worktree expectations
6. `.codex/README.md` and `.codex/memories/00_ACTIVE.md` -- Codex control-plane alignment when comparing or sharing workflows
7. `autodoc/AGENT_INDEX.md` -- fast agent seam map
8. `docs/agentic/QUESTION_PROTOCOL.md` and `docs/agentic/FAILURE_LEDGER.md` -- blockers, assumptions, failures, and workarounds

Precedence when instructions conflict: `docs/STATUS.md` > `AGENTS.md` > this file.

## Essential Commands

### Backend (.NET 8)

```bash
dotnet restore backend/Taskdeck.sln
dotnet build backend/Taskdeck.sln -c Release
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Run a single backend test class:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~MyTestClassName"
```

### Frontend (Vue 3 + Vite, Node 24.x)

```bash
cd frontend/taskdeck-web
npm install
npm run dev          # dev server on :5173
npm run typecheck    # vue-tsc type checking
npm run build        # typecheck + vite build
npx vitest --run --reporter=verbose   # unit tests
npx vitest --run -t "test name"       # single test by name
npm run lint         # eslint
```

E2E (Playwright):
```bash
cd frontend/taskdeck-web
npx playwright test --reporter=line
npx playwright test tests/e2e/some-spec.spec.ts   # single E2E file
```

### Docker (from repo root)

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

### Default URLs

- API: `http://localhost:5000` | Swagger: `http://localhost:5000/swagger` | Frontend: `http://localhost:5173`

## Architecture

### Backend -- Clean Architecture layers in `backend/src/`

- **Taskdeck.Domain**: Core entities and business rules. No infrastructure dependencies -- keep it pure.
- **Taskdeck.Application**: Use cases and services. Depends only on Domain.
- **Taskdeck.Infrastructure**: Persistence (EF Core + SQLite), external adapters. Implements interfaces defined in Application/Domain.
- **Taskdeck.Api**: ASP.NET Core HTTP endpoints, integration layer, auth, SignalR hubs. Wires everything up via DI.
- **Taskdeck.Cli**: CLI entry point (separate from API).

Tests mirror this layout in `backend/tests/` (`Domain.Tests`, `Application.Tests`, `Api.Tests`, `Cli.Tests`, `Integration.Tests`, `Architecture.Tests`).

### Frontend -- `frontend/taskdeck-web/src/`

- **views/**: Route-level pages (BoardView, InboxView, ReviewView, AutomationChatView, TodayView, HomeView, etc.). Large views are decomposed into thin shells (<300 lines) that delegate to extracted components and composables.
- **store/**: Pinia stores -- boardStore, captureStore, queueStore, sessionStore, workspaceStore, notificationStore, etc.
- **api/**: HTTP client modules for backend communication
- **composables/**: Shared Vue composition functions (including view-specific orchestrators like useReviewProposals, useInboxOrchestrator, useAutomationChat, useBoardDragDrop, useSessionTimeout, etc.)
- **components/**: Reusable UI components (shared Td* primitives in `components/ui/`, plus view-specific components extracted from decomposed views)
- **router/**: Vue Router configuration

Uses Tailwind CSS, TypeScript, and Vue 3 composition API (`<script setup>`).

### Key Data Flow

1. User captures input → captureStore → backend inbox API
2. System generates a proposal (structured board change)
3. User reviews proposal in ReviewView
4. Explicit approval applies changes to board via boardStore

### Realtime

SignalR (`@microsoft/signalr`) provides realtime board collaboration.

### LLM Providers

Mock provider is default. OpenAI and Gemini supported behind config gates. See `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`.

## Work Protocol

- Before edits: write a short plan (files, approach, risks, tests).
- Keep diffs small and scoped; avoid large mixed refactors.
- After edits: run required checks and report results.
- For product-facing slices, ensure scope aligns with the thesis (reduce maintenance overhead/capture friction, preserve review-first trust).
- Use `.claude/skills/README.md` to pick a local Claude skill for broad, issue, PR-review, CI-recovery, backend, frontend, capture/review, demo, or verification work.
- Use `docs/agentic/QUESTION_PROTOCOL.md` before asking; batch true blockers and proceed with explicit assumptions for reversible choices.
- Use `docs/agentic/FAILURE_LEDGER.md` for unresolved command/tool/test/CI failures and promote recurring lessons through `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`.
- Use spawned subagents or Claude worktree agents when the situation warrants it (genuinely disjoint work, an independent review lens, or context beyond one window) — you don't need to be asked, but right-size the fan-out (start inline; a few agents, not a reflexive fleet) and never delegate final synthesis or verification: one coordinator owns those. Sizing rules: the global `model-effort-routing` skill.

## Review Policy

Every code review — self-review, adversarial review, subagent review — follows these rules:

1. **Post findings on the PR.** Unless the user explicitly says otherwise, when a review targets a PR, post a comment with all findings organized by severity.
2. **Fix everything found.** Every finding at every severity (CRITICAL, HIGH, MEDIUM, LOW) must be addressed. No "non-blocking" dismissals. Do not skip lower-priority findings.
3. **Out-of-scope findings get tracked.** If a finding is real but outside the PR's scope, seed a GitHub issue. Never silently drop it.
4. **Inspect all existing PR comments.** Before posting findings, read ALL comments — human reviews, bot comments, previous review threads. Address anything unaddressed.
5. **Post fix evidence.** After fixing findings, post a follow-up comment mapping each finding to its fix commit and verification.

## Definition of Done

- Behavior changes ship with tests (unit/integration/E2E as appropriate).
- Handle error cases explicitly; do not swallow failures.
- Update docs when reality changes: `docs/STATUS.md` for current state, `docs/IMPLEMENTATION_MASTERPLAN.md` for delivery history and planned work.
- HTTP semantics: use stable codes (401/403/404/409). Claims-first identity.

## Coding Conventions

- **Backend**: C# conventions, 4-space indent, PascalCase for public members, camelCase for locals. Respect layer boundaries (Domain must not reference Infrastructure).
- **Frontend**: TypeScript + Vue SFCs in PascalCase. Use `<script setup>` and composition API. Meaningful names over abbreviations.
- **Commits**: Present-tense, small, focused. One commit per file when spanning multiple files. File move/rename batches are fine as single commits.
- **DCO sign-off**: Every new commit, including a merge commit, must include a `Signed-off-by:` trailer. In automated/background terminals, use `git commit -s --no-gpg-sign` for ordinary commits, `git merge --signoff --no-gpg-sign BRANCH_NAME` for a clean commit-producing merge (replace `BRANCH_NAME` with the source ref), and `git commit -s --no-gpg-sign --no-edit` after staging conflict resolutions; `-s`/`--signoff` adds the DCO trailer while `--no-gpg-sign` avoids hidden GPG pinentry. Never use `--no-verify`; hooks must run, and failures must be investigated.

## Testing Guidelines

- Mirror production namespaces in test namespaces and file names.
- Backend tests: project-per-layer in `backend/tests/` (Domain.Tests, Application.Tests, Api.Tests, Architecture.Tests).
- Frontend: vitest for unit tests, Playwright for E2E. See `docs/TESTING_GUIDE.md`.

## CI

Reusable GitHub Actions workflows under `.github/workflows/`. `ci-required.yml` is the gate for PRs. Nightly extended checks in `ci-nightly.yml`.

## Architecture Decision Records (ADRs)

ADRs live in `docs/decisions/`. See `docs/decisions/README.md` for the template and conventions.

**When to create an ADR**: Write one when a decision chooses between competing approaches, establishes a project-wide constraint, has hard-to-reverse consequences, or would surprise a future contributor. This includes technology selections, data model choices, security posture changes, automation safety boundaries, and strategic product pivots.

**How to create an ADR**: Use the next available number (`ADR-NNNN`), follow the template (Context, Decision, Alternatives, Consequences, References), and add the entry to `docs/decisions/INDEX.md`. Mark status as `Proposed` until ratified, then `Accepted`.

**Do not skip ADRs** for decisions that affect architecture, security posture, or cross-cutting conventions -- even when the change is small, the reasoning matters for future contributors who weren't in the conversation.

## Key Docs

- `docs/STATUS.md` -- current shipped reality (what is true now)
- `docs/IMPLEMENTATION_MASTERPLAN.md` -- delivery history, roadmap, and planned work (what was done and what comes next)
- `docs/REVIVAL_PLAN.md` -- **the active planning spine** (2026-07-10 revival pivot, ADR-0044): positioning, business posture, phased waves, the v0.1 ship gate, issue map, traction checkpoint
- `docs/analysis/2026-07-10_revival_assessment.md` -- the revive-vs-archive evidence base (code review + market research, adversarially verified) behind ADR-0044
- `docs/PROJECT_TRAJECTORY.md` -- 2026-07-02 whole-project analysis: strengths, pivot-goal scoring, and the remaining path to archive (superseded direction; retained as the checkpoint fallback + evidence)
- `docs/COURSE_CORRECTION.md` -- 2026-07-02 whole-project analysis: what must change (strategy + execution); its finite-work discipline carries into the revival ship gate
- `docs/GOLDEN_PRINCIPLES.md` -- stable invariants
- `docs/decisions/INDEX.md` -- architecture decision records
- `docs/TESTING_GUIDE.md` -- test operations reference
- `docs/ISSUE_EXECUTION_GUIDE.md` -- dependency-aware issue execution order
- `docs/MCP_TOOLING_GUIDE.md` -- MCP tool selection rules
- `autodoc/AGENT_INDEX.md` -- fast agent seam map and context traps
- `docs/agentic/` -- question, failure, guide-update, and skill-registry protocols
- `docs/platform/CONFIGURATION_REFERENCE.md` -- appsettings/env var/Docker Compose reference for every backend setting
- `docs/platform/EF_MIGRATION_WORKFLOW.md` -- EF Core migration operations and best practices
- `AGENTS.md` -- full contributor protocol

## Worktree Isolation for Parallel Agents

When launching subagents with `isolation: "worktree"`, follow the protocol in `docs/WORKTREE_AGENT_PROTOCOL.md`. Key rules:
- NEVER include absolute paths to the main checkout in worktree agent prompts
- First agent action: run the inline worktree guard from the protocol
- All file paths must use the exported `$WT_PROJECT_DIR` variable
- Shell state does not persist between Bash tool calls -- agents must use absolute paths
- After agents complete, verify main checkout is still clean on the default branch

## Windows Notes

- Run `bash scripts/check-git-env.sh` to validate git resolution and index.lock state before a work session.
- If `git` resolves to Cygwin or fails with signal errors, use `C:\Program Files\Git\cmd\git.exe` explicitly (or add `C:\Program Files\Git\cmd` to the front of `PATH`).
- Do not chain commands with `&&` in PowerShell; use `;` and check `$LASTEXITCODE`.
- If `.git/index.lock` blocks commits, check for active git processes before removing it. The `check-git-env.sh` script automates this check.
