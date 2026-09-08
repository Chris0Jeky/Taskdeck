# Taskdeck Frontend (Vue3/TS)

## Rules
- Centralize HTTP in src/api. No endpoint URLs in views/components.
- Always implement: loading + empty + error + disabled states.
- Handle auth consistently: 401 => session flow, 403 => permission message.
- Keep diffs small; avoid refactors unless needed for the change.

## MCP usage (frontend)
- For Vue/Vite/TS questions: use Context7 docs lookups before guessing.
- For UI regression: use the selected available browser controller (Chrome DevTools by project default) to reproduce and capture screenshots. Use repository Playwright tests for durable regression coverage; prefer stable selectors and avoid sleeps.
- For repo-wide searching: prefer native `rg`; if shell search is unavailable, Codex uses GitHub MCP `search_code` and Claude uses `gh search code`, as documented in `docs/MCP_TOOLING_GUIDE.md`.
- CI parity: from the repository root in PowerShell, run `Set-Location frontend/taskdeck-web; if (-not $?) { exit 1 }; npm run typecheck; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; npm run build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; npx vitest --run --maxWorkers=2` for frontend changes.

## Required checks (from frontend/taskdeck-web)
npm run typecheck
npm run build
npx vitest --run --maxWorkers=2
