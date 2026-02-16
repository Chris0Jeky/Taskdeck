# Repository Guidelines

This document is a concise contributor guide for the Taskdeck repository.
Its scope applies to the entire repo unless overridden by more specific `AGENTS.md` files in subfolders.

## Always start here (required)
- Read `docs/STATUS.md` for Current Focus and constraints (source of truth).
- Use `docs/IMPLEMENTATION_MASTERPLAN.md` for roadmap context.
- For test operations, see `docs/TESTING_GUIDE.md`.
- Precedence when instructions conflict: `docs/STATUS.md` > this file > subfolder `AGENTS.md`.

## MCP tools (agent tooling)
- See `docs/MCP_TOOLING_GUIDE.md` for tool selection rules and safe usage.
- Prefer MCP tools over guessing:
    - OpenAI/Codex/API docs → openaiDeveloperDocs MCP
    - Third-party library docs → Context7 MCP
    - UI reproduction/regression → Playwright MCP
    - Repo search → native `rg` (ripgrep MCP unreliable on Windows; fallback to GitHub search_code)
    - Issues/PRs/workflows → GitHub MCP (write actions only when required)

## Project Operations Automation (required)
- Read `docs/GITHUB_PROJECT_AUTOMATION.md` before changing project operations, issue templates, or workflow conventions.
- Canonical project status model is: `Pending`, `Now`, `Next`, `Blocked`, `Review`, `Done`.
- Do not introduce labels in issue templates that are not in the required label set documented in `docs/GITHUB_PROJECT_AUTOMATION.md`.
- If workflow/status conventions change, update both:
  - `docs/GITHUB_PROJECT_AUTOMATION.md`
  - this `AGENTS.md` (only if contributor behavior expectations changed)

## Work protocol (required)
- Before edits: write a short plan (files, approach, risks, tests).
- Keep diffs small and scoped; avoid large mixed refactors.
- After edits: run required checks and report results.
- If you cannot run checks, state exactly why and what you would run.

### Small Mainline Exception
- If a change is very small and low-risk (especially minor docs wording/checklist updates), do not automatically create a branch/PR.
- Prompt the user first and offer to let the user apply the change directly on `main`.
- Only proceed with branch/PR flow for these tiny changes when the user explicitly asks for it.

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
  (`Taskdeck.Api`, `Taskdeck.Application`, `Taskdeck.Domain`, `Taskdeck.Infrastructure`).
- Backend tests: `backend/tests` with project-per-layer test suites.
- Frontend (Vue 3 + Vite): `frontend/taskdeck-web` with app source in `src`, static assets in `public`.
- Docs and planning: Start with `docs/STATUS.md` (source of truth),
  `docs/IMPLEMENTATION_MASTERPLAN.md` (active roadmap), and `docs/TESTING_GUIDE.md` (test operations).
  Historical context lives under `docs/archive/`.

## Build, Test & Run
- Backend restore/build: `dotnet restore backend/Taskdeck.sln` and `dotnet build backend/Taskdeck.sln -c Release`.
- Backend tests (required): `dotnet test backend/Taskdeck.sln -c Release`.
- Backend API (local): from `backend/src/Taskdeck.Api`, run `dotnet run`.
- Frontend dev server: from `frontend/taskdeck-web`, run `npm install` once, then `npm run dev`.
- Frontend checks (required when frontend touched): from `frontend/taskdeck-web`,
  `npm run typecheck && npm run build && npx vitest --run`.

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
  Default to one commit per changed file with a short file-specific description.
  Keep commits small and focused; avoid large mixed-topic commits.
  If a single logical change must touch multiple files, keep the smallest practical commit set and explain why.
- Pull requests: Provide a short summary, key implementation notes, and testing evidence
  (commands run, screenshots for UI changes). Link related issues/tasks where applicable.
- Keep PRs focused and small when possible; prefer follow-up PRs for refactors or additional cleanup.

## Output expectations (after work)
Provide:
- Summary of changes
- Files touched
- Tests added/updated
- Commands run + results
- Docs updated (`STATUS` / `MASTERPLAN`)
- Notable risks or follow-ups (if any)

See docs/FUTURE_HARNESS_BACKLOG.md for deferred harness/MCP upgrades.

See docs/DEVTOOLS_OBSERVABILITY_ADDON.md for Playwright vs DevTools vs logs decision rules.
