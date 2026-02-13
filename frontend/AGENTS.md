# Taskdeck Frontend (Vue3/TS)

## Rules
- Centralize HTTP in src/api. No endpoint URLs in views/components.
- Always implement: loading + empty + error + disabled states.
- Handle auth consistently: 401 => session flow, 403 => permission message.
- Keep diffs small; avoid refactors unless needed for the change.

## Required checks (from frontend/taskdeck-web)
npm run typecheck
npm run build
npx vitest --run
