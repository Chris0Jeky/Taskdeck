# Taskdeck Frontend (Vue3/TS)

## Rules
- Centralize HTTP in src/api. No endpoint URLs in views/components.
- Always implement: loading + empty + error + disabled states.
- Handle auth consistently: 401 => session flow, 403 => permission message.
- Keep diffs small; avoid refactors unless needed for the change.

## MCP usage (frontend)
- For Vue/Vite/TS questions: use Context7 docs lookups before guessing.
- For UI regression: use Playwright MCP to reproduce and capture screenshots; prefer stable selectors and avoid sleeps.
- For repo-wide searching: prefer native `rg`; fallback to GitHub MCP search_code.
- CI parity: always run `npm run typecheck && npm run build && npx vitest --run` for frontend changes.

## Required checks (from frontend/taskdeck-web)
npm run typecheck
npm run build
npx vitest --run
