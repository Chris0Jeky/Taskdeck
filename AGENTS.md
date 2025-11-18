# Repository Guidelines

This document is a concise contributor guide for the Taskdeck repository. Its scope applies to the entire repo unless overridden by more specific `AGENTS.md` files in subfolders.

## Project Structure & Modules

- Backend (.NET): `backend/Taskdeck.sln` with layered projects under `backend/src` (`Taskdeck.Api`, `Taskdeck.Application`, `Taskdeck.Domain`, `Taskdeck.Infrastructure`).
- Backend tests: `backend/tests` with project-per-layer test suites.
- Frontend (Vue 3 + Vite): `frontend/taskdeck-web` with app source in `src`, static assets in `public`.
- Docs and planning: Top-level `README.md`, `IMPLEMENTATION_STATUS.md`, `TEST_SUITE_PLAN.md`, and related files describe roadmap and test strategy.

## Build, Test & Run

- Backend restore/build: `dotnet restore backend/Taskdeck.sln` and `dotnet build backend/Taskdeck.sln -c Release`.
- Backend tests: `dotnet test backend/Taskdeck.sln` (run before every PR).
- Backend API (local): from `backend/src/Taskdeck.Api`, run `dotnet run`.
- Frontend dev server: from `frontend/taskdeck-web`, run `npm install` once, then `npm run dev`.
- Frontend build: `npm run build` in `frontend/taskdeck-web` (outputs to `dist`).

## Coding Style & Naming

- Backend: C# 10+ conventions, 4-space indentation, PascalCase for classes and public members, camelCase for locals and parameters. Keep layers pure (e.g., `Domain` has no infrastructure dependencies).
- Frontend: TypeScript + Vue SFCs in `PascalCase.vue`. Use script setup and composition APIs where existing code does. Prefer meaningful names over abbreviations.
- Keep formatting consistent with existing files; do not introduce new style tools without discussion.

## Testing Guidelines

- Prefer unit tests close to the corresponding project (e.g., `Taskdeck.Application.Tests` for `Taskdeck.Application` logic).
- Mirror production namespaces in test namespaces and file names (e.g., `FooServiceTests.cs` for `FooService`).
- For frontend, add tests following the existing tooling (or document gaps clearly in PRs if tests are not yet present).
- Aim to cover new branches and error paths, especially in application services and HTTP endpoints.

## Commit & Pull Request Guidelines

- Commits: Use clear, present-tense messages (e.g., `Add booking validation to application layer`). Group related changes; avoid large mixed-topic commits.
- Pull requests: Provide a short summary, key implementation notes, and testing evidence (commands run, screenshots for UI changes). Link related issues or tasks where applicable.
- Keep PRs focused and small when possible; prefer follow-up PRs for refactors or additional cleanup.

