# Quality Gates + Visual Testing (Frontend)
Date: 2026-02-23
Status: Draft

Your backend quality gates are strong. The frontend needs similar enforcement to keep UI work stable as you move fast.

---

## Mandatory gates (recommended)
### 1) Lint + format
- ESLint with `eslint-plugin-vue` supports Vue 3 `<script setup>`.  
  https://eslint.vuejs.org/user-guide/
- Add Prettier to reduce style debates and make diffs smaller.

### 2) Typecheck gate
- `vue-tsc` already exists; run in CI.

### 3) Unit tests
- Vitest already exists; enforce a baseline coverage threshold for critical folders:
  - `src/components/ui`
  - `src/stores`
  - `src/api`

### 4) E2E smoke
- Playwright already exists; keep one “golden path” test per major flow.

---

## Visual regression testing (high leverage for UI polish)
Playwright provides screenshot assertions (`toHaveScreenshot`) that generate baselines on first run and compare on subsequent runs.  
https://playwright.dev/docs/test-snapshots

Recommended approach:
- Add 5–10 stable screenshot tests for core screens:
  - AppShell (sidebar open/closed)
  - Board view (empty, populated)
  - Card modal
  - Inbox list/detail
  - Proposal review
- Disable animations for screenshot tests.

---

## Storybook (optional, strongly recommended)
Storybook enables component-driven development and docs for UI primitives.
- Vue3 + Vite framework docs: https://storybook.js.org/docs/get-started/frameworks/vue3-vite
- Component-driven methodology explanation: https://storybook.js.org/tutorials/intro-to-storybook/vue/en/simple-component/

Use Storybook when:
- you are building many primitives
- you want consistent states and documentation
- you want easier visual review and collaboration

---

## Accessibility testing (optional)
- Add Playwright + axe-core later for automated checks.
Start simple:
- do manual keyboard scripts first
- add automated rules once UI stabilizes

---

## CI lane suggestion (frontend)
- `frontend:lint` (eslint)
- `frontend:typecheck` (vue-tsc)
- `frontend:test` (vitest)
- `frontend:e2e-smoke` (playwright)
- `frontend:visual` (optional; screenshot tests)

Keep lane names stable and required checks consistent.
