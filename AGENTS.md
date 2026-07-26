# Repository Guidelines

This document is a concise contributor guide for the Taskdeck repository.
Its scope applies to the entire repo unless overridden by more specific `AGENTS.md` files in subfolders.

## Always start here (required)
- Read `OUTSTANDING_TASKS.md` (repo root) — the maintainer's durable cross-session checklist. Surface its open items in any summary/status/handoff; only check items off when the maintainer says they are done.
- Read `docs/STATUS.md` for Current Focus and constraints (source of truth).
- Use `docs/IMPLEMENTATION_MASTERPLAN.md` for roadmap context.
- Use `docs/GOLDEN_PRINCIPLES.md` for stable repository invariants and guardrails.
- Use `docs/ISSUE_EXECUTION_GUIDE.md` for dependency-aware issue execution order.
- For Codex routing, read `.codex/README.md` and `.codex/memories/00_ACTIVE.md`.
- For Claude Code routing, read `.claude/README.md` and `CLAUDE.md`.
- For test operations, see `docs/TESTING_GUIDE.md`.
- Precedence when instructions conflict: `docs/STATUS.md` > this file > subfolder `AGENTS.md`.

## MCP tools (agent tooling)
- See `docs/MCP_TOOLING_GUIDE.md` for tool selection rules and safe usage.
- For high-autonomy Codex issue/PR/CI batches, see `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`.
- For low-context agent orientation, use `autodoc/AGENT_INDEX.md`.
- For blocker questions, unresolved failures, guide updates, and skill routing, use `docs/agentic/`.
- MCP-first default: when an MCP tool can perform a task, use MCP before shell/CLI alternatives.
- Prefer MCP tools over guessing:
    - OpenAI/Codex/API docs -> openaiDeveloperDocs MCP
    - Third-party library docs -> Context7 MCP
    - UI reproduction/regression -> Playwright MCP
    - Repo search -> native `rg` (ripgrep MCP unreliable on Windows; fallback to GitHub search_code)
    - Issues/PRs/workflows -> GitHub MCP (write actions only when required)
- Fallback rule:
  - if MCP is unavailable, failing, or lacks required capability, use shell/CLI fallback and state that fallback briefly in handoff notes.

## Multi-Agent / Parallel Execution (required)
- When the Codex runtime exposes spawned subagents, use them without asking for extra permission when they are efficient for safely parallelizable work — but right-size the fan-out (start inline; a few agents, not a reflexive fleet), and keep one coordinator for synthesis and final verification.
- Split only when work can be separated by clear ownership and can proceed without blocking the coordinator's immediate next step.
- If spawned agents are unavailable, use explicit git worktrees plus separate Codex/Claude sessions or GitHub coding-agent tasks; do not claim subagent execution unless it actually happened.
- Split ownership by file/module/concern so concurrent work does not overlap.
- Keep one coordinator responsible for issue selection, synthesis, conflict resolution, docs rehydration, project status/priority sync, and final verification.
- Do not delegate final synthesis, ownership decisions, or broad vague cleanup.
- For batch issue execution, PR review loops, CI recovery, and docs reconciliation, follow `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`.

## Project Operations Automation (required)
- Read `docs/GITHUB_PROJECT_AUTOMATION.md` before changing project operations, issue templates, or workflow conventions.
- Canonical project status model is: `Pending`, `Now`, `Next`, `Blocked`, `Review`, `Done`.
- Do not introduce labels in issue templates that are not in the required label set documented in `docs/GITHUB_PROJECT_AUTOMATION.md`.
- Priority sync is mandatory:
  - For issue project items, set project `Priority` to match the issue's single priority label (`Priority I` to `Priority V`).
  - For PR project items, derive `Priority` from linked/referenced issues; if multiple priorities exist, choose highest urgency (`I` highest); if none can be derived, set `Priority V`.
  - Before handoff, verify no issue/PR project item has empty `Priority`.
- If workflow/status conventions change, update both:
  - `docs/GITHUB_PROJECT_AUTOMATION.md`
  - this `AGENTS.md` (only if contributor behavior expectations changed)

## Local skill packs
- Repo-local Codex skills live under `.codex/skills/` and supplement `AGENTS.md`; they do not override it. Start with `.codex/skills/README.md`.
- Repo-local Claude Code skills live under `.claude/skills/` and mirror the same Taskdeck workflows for Claude sessions. Start with `.claude/skills/README.md`.
- Use `.codex/skills/taskdeck-issue-batch-orchestrator` when asked to take care of many issues, pick issues, coordinate worktrees/subagents, open PRs, run review loops, or reconcile a high-autonomy batch.
- Use `.codex/skills/taskdeck-worktree-issue-worker` when implementing a single assigned issue in an isolated worktree.
- Use `.codex/skills/taskdeck-pr-review-loop` when reviewing PRs, spinning fresh adversarial reviewers, posting findings, or addressing review/bot comments.
- Use `.codex/skills/taskdeck-ci-conflict-recovery` when checking failing CI, resolving PR conflicts, inspecting bot comments, or recovering blocked PRs.
- Use `.codex/skills/taskdeck-repo-onramp` when the request is broad, the repo area is unfamiliar, or current Taskdeck reality must be reconciled before planning.
- Use `.codex/skills/taskdeck-backend-slice` for backend/API/application/infrastructure/worker/auth behavior changes.
- Use `.codex/skills/taskdeck-frontend-workspace-slice` for frontend shell, workspace, route, help-state, and novice-legibility work outside the core capture-review semantics.
- Use `.codex/skills/taskdeck-capture-review-loop` when capture, inbox, proposal review, execute flow, provenance, or board handoff semantics are involved.
- Use `.codex/skills/taskdeck-demo-regression` when a task needs the right evidence path, seeded demo state, Playwright proof, or stakeholder-facing walkthrough validation.
- Use `.codex/skills/taskdeck-verification-doc-sync` at the end of implementation to choose the right checks, update only the right docs, and prepare the required Taskdeck handoff summary.
- Use `.codex/skills/taskdeck-question-batch` when ambiguity needs a blocker/assumption decision instead of context-expensive question loops.
- Use `.codex/skills/taskdeck-failure-capture` when failed tools, tests, CI, MCP, docs checks, or workarounds must be classified and surfaced.
- Use `.codex/skills/taskdeck-interface-map` when adding, splitting, or documenting complex seams that should be findable through `autodoc/AGENT_INDEX.md`.

## Work protocol (required)
- Before edits: write a short plan (files, approach, risks, tests).
- Keep diffs small and scoped; avoid large mixed refactors.
- Ask only for true blockers; otherwise proceed with explicit assumptions. Use `docs/agentic/QUESTION_PROTOCOL.md`.
- Do not silently bury failed commands or tool workarounds. Use `docs/agentic/FAILURE_LEDGER.md` when unresolved failures matter for future agents or handoff confidence.
- Prefer incremental execution with incremental, file-scoped commits when the work spans multiple files or concerns.
- After edits: run required checks and report results.
- If you cannot run checks, state exactly why and what you would run.
- In this Windows PowerShell environment, do not chain commands with `&&`; use `;` and check `$LASTEXITCODE` when failure handling matters.
- For product-facing slices, ensure issue scope and acceptance criteria explicitly align with the current thesis (reduce maintenance overhead/capture friction and preserve review-first trust).

### Windows Git Reliability Fallback
- Run `bash scripts/check-git-env.sh` at the start of a session to validate git resolution and index.lock state.
- In PowerShell/Codex-native sessions, prefer `powershell -File scripts/check-git-env.ps1`; the Bash script remains available for Bash shells.
- If `git` resolves to Cygwin or produces signal/pipe-style failures, use `C:\Program Files\Git\cmd\git.exe` explicitly for repo operations (or add `C:\Program Files\Git\cmd` to the front of `PATH`).
- Every new commit must include a `Signed-off-by:` trailer. In automated/background terminals, use `git commit -s --no-gpg-sign` so `-s` adds the DCO trailer while `--no-gpg-sign` avoids hidden GPG pinentry. Never use `--no-verify`; hooks must run, and failures must be investigated.
- If a commit fails because `.git/index.lock` cannot be created, first check for active `git` processes; remove `.git/index.lock` only when no git process is running. The `check-git-env.sh` script automates this detection.
- For stacked branches with small conflict surfaces, prefer `merge` over `rebase` when branch reconciliation starts stalling (for example long-running interactive/conflict loops). Resolve conflicts once, merge, and continue delivery.

### Codex Worktree Safety
- Use `scripts/git/New-CodexIssueWorktree.ps1` to create isolated issue worktrees under `.worktrees/`.
- First command in a Codex worktree worker session must be `powershell -File scripts/worktree_guard.ps1` (or `source scripts/worktree_guard.sh` in Bash).
- Do not pass absolute main-checkout paths to worktree workers; derive paths from `$env:WT_PROJECT_DIR` or `git rev-parse --show-toplevel`.
- Only the coordinator should update canonical batch docs such as `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, and `docs/TESTING_GUIDE.md` unless a worker explicitly owns a docs-only issue.

### Small Mainline Exception
- If a change is very small and low-risk (especially minor docs wording/checklist updates), do not automatically create a branch/PR.
- If the user already asked for the change directly, it may be applied on the current branch after checking git status and keeping the diff narrow.
- If the user did not clearly ask for the change, prompt first and offer to let the user apply it directly on `main`.
- Only proceed with branch/PR flow for these tiny changes when the user explicitly asks for it.

## Review Policy (non-negotiable)

Every code review — self-review, adversarial review, subagent review — follows these rules:

1. **Post findings on the PR.** Unless the user explicitly says otherwise, when a review targets a PR, post a comment with all findings organized by severity. This is the default, not optional.
2. **Fix everything found.** Every finding at every severity (CRITICAL, HIGH, MEDIUM, LOW) must be addressed with a fix, commit, and verification. There is no "non-blocking" category that gets ignored. Do not skip lower-priority findings.
3. **Out-of-scope findings get tracked, not buried.** If a finding is real but drifts outside the PR's scope, document it and seed a GitHub issue (or add to an existing tracking issue). Never silently drop it. Tech debt from reviews must be zero.
4. **Inspect all existing PR comments.** Before posting findings, read ALL comments on the PR — human reviews, bot comments (Dependabot, CodeQL, CI bots), and previous adversarial review threads. Address anything unaddressed: fix it, reply with invalidation evidence, or seed a tracked follow-up.
5. **Post fix evidence.** After fixing findings, post a follow-up comment mapping each finding to its fix commit and verification result.

These rules apply equally to Claude and Codex agents, subagents, and worktree workers.

## Definition of Done (non-negotiable)
- Behavior changes ship with tests (unit/integration/E2E as appropriate).
- Handle error cases explicitly; do not swallow failures.
- Update docs when reality changes:
    - `docs/STATUS.md` (what is true now)
    - `docs/IMPLEMENTATION_MASTERPLAN.md` (roadmap impact / next steps)

## Security baseline (always on)
- Never trust client input for identity/authority.
- Enforce authn/authz consistently for protected resources.
- Validate inputs server-side; fail safely.
- Do not log secrets, tokens, or sensitive user data.

## Project Structure & Modules
- Backend (.NET): `backend/Taskdeck.sln` with layered projects under `backend/src`
  (`Taskdeck.Api`, `Taskdeck.Application`, `Taskdeck.Domain`, `Taskdeck.Infrastructure`, `Taskdeck.Cli`).
- Backend tests: `backend/tests` with project-per-layer test suites.
- Frontend (Vue 3 + Vite): `frontend/taskdeck-web` with app source in `src`, static assets in `public`.
- Docs and planning: Start with `docs/STATUS.md` (source of truth),
  `docs/IMPLEMENTATION_MASTERPLAN.md` (active roadmap), `docs/GOLDEN_PRINCIPLES.md` (stable invariants), and `docs/TESTING_GUIDE.md` (test operations).
  Historical context lives under `docs/archive/`.

## Build, Test & Run
- Backend restore/build: `dotnet restore backend/Taskdeck.sln` and `dotnet build backend/Taskdeck.sln -c Release`.
- Backend tests (required): `dotnet test backend/Taskdeck.sln -c Release -m:1`.
- Backend API (local): from `backend/src/Taskdeck.Api`, run `dotnet run`.
- Frontend dev server: from `frontend/taskdeck-web`, run `npm install` once, then `npm run dev`.
- Frontend checks (required when frontend touched): from `frontend/taskdeck-web`,
  `npm run typecheck; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; npm run build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; npx vitest --run` (PowerShell fail-fast; do not use `&&`).

## Coding Style & Naming
- Backend: C# conventions, 4-space indentation, PascalCase for classes and public members,
  camelCase for locals and parameters. Keep layers pure (e.g., `Domain` has no infrastructure dependencies).
- Frontend: TypeScript + Vue SFCs in `PascalCase.vue`. Use script setup and composition APIs where existing code does.
  Prefer meaningful names over abbreviations.
- Keep formatting consistent with existing files; do not introduce new style tools without discussion.

## Testing Guidelines
- Prefer unit tests close to the corresponding project (e.g., `Taskdeck.Application.Tests` for application logic).
- Mirror production namespaces in test namespaces and file names (e.g., `FooServiceTests.cs` for `FooService`).
- Add coverage for new branches and error paths, especially in application services and HTTP endpoints.
- For frontend, add tests following the existing tooling (or document gaps clearly if tests are not yet present).

## Commit & Pull Request Guidelines
- Commits: Use clear, present-tense messages (e.g., `Add booking validation to application layer`).
  Default to small focused incremental commits, often one commit per changed file with a short file-specific description.
  Keep commits small and focused; avoid large mixed-topic commits.
  If a single logical change must touch multiple files, keep the smallest practical commit set and explain why.
  Exception: for pure file move/rename batches (no content changes), one grouped commit is acceptable and preferred.
- Pull requests: Provide a short summary, key implementation notes, and testing evidence
  (commands run, screenshots for UI changes). Link related issues/tasks where applicable.
- Keep PRs focused and small when possible; prefer follow-up PRs for refactors or additional cleanup.
- PRs touching CI workflows (`.github/workflows/`), infrastructure (`deploy/`, `scripts/`), or project files (`*.csproj`) auto-trigger CI Extended. Ensure CI Extended is green before merging these PRs.
- For issue execution unless the user explicitly says otherwise: open the PR after verification is complete, then perform a deliberate reviewer-style pass on the PR diff/comments before handoff. Follow the Review Policy: post findings, fix everything, address all existing comments, and seed issues for out-of-scope items. Follow the Review Policy: post findings, fix everything, address all existing comments, and seed issues for out-of-scope items.

## Output expectations (after work)
Provide:
- Summary of changes
- Files touched
- Tests added/updated
- Commands run + results
- Docs updated (`STATUS` / `MASTERPLAN`)
- Notable risks or follow-ups (if any)

See `docs/tooling/FUTURE_HARNESS_BACKLOG.md` for deferred harness/MCP upgrades.

See `docs/tooling/DEVTOOLS_OBSERVABILITY_ADDON.md` for Playwright vs DevTools vs logs decision rules.
