# Issue Seeds — Frontend Premium UI Wave
Date: 2026-02-23
Status: Draft

This wave is meant to be executed incrementally alongside the Capture MVP work.
Do NOT start a massive global reskin without primitives.

## Epic
### UI-00 — Frontend Premium UI Wave (foundations → primitives → screens)
Labels: `Priority III`, `frontend`, `ux`, `design-system`, `testing`
DoD:
- UI-01..UI-12 closed
- At least one critical flow feels “premium”: Board OR Inbox end-to-end
- Manual checklist updated with keyboard + focus scripts
- Visual regression baseline created for at least 3 screens

---

## Foundations
### UI-01 — Design tokens: semantic surfaces + dark theme + density modes
Labels: `Priority III`, `frontend`, `design-system`
AC:
- token namespaces documented
- light/dark theme supported via `data-theme`
- density supported via `data-density`
- no new hard-coded colors in new components
Tests:
- unit tests for theme toggles if applicable
- screenshots for light/dark shell

### UI-02 — UI primitives library: Button/Input/Dialog/Popover/Toast/Skeleton
Labels: `Priority III`, `frontend`, `design-system`, `ux`
Depends: UI-01
AC:
- primitives exist under `src/components/ui`
- each primitive has at least 1 story or unit test
- consistent states (hover/focus/disabled/loading)

### UI-03 — Library decision spike: Radix Vue vs shadcn-vue vs Headless UI
Labels: `Priority III`, `frontend`, `ux`
AC:
- 3 primitives implemented in a spike branch
- decision recorded in docs (short ADR)
- chosen approach added to MASTERPLAN

---

## Shell + Navigation
### UI-04 — AppShell reskin to use tokens + primitives (no behavior changes)
Labels: `Priority III`, `frontend`, `ux`, `refactor`
Depends: UI-01, UI-02
AC:
- sidebar/nav styles use tokens (no raw rgba)
- buttons/menus use primitives
- keyboard shortcuts help remains accessible

---

## Board UX polish
### UI-05 — Board card component polish (density, typography, states)
Labels: `Priority III`, `frontend`, `ux`
Depends: UI-02
AC:
- card states consistent (hover/focus/selected)
- labels/badges standardized
- improved spacing and readability

### UI-06 — Drag/drop premium behavior (DnD library adoption or hardened custom)
Labels: `Priority III`, `frontend`, `ux`, `performance`, `a11y`
AC:
- drag handle targets meet WCAG target size guidance (24x24)
- keyboard alternative for move exists (WCAG 2.5.7)
- drag does not cause stutter

References:
- WCAG 2.5.7 + 2.5.8: https://www.w3.org/TR/WCAG22/
- Pragmatic DnD: https://github.com/atlassian/pragmatic-drag-and-drop

---

## Inbox (Capture) UX
### UI-07 — Inbox list/detail view built on primitives (premium empty/loading states)
Labels: `Priority III`, `frontend`, `ux`
Depends: UI-02, CAP-02..CAP-05
AC:
- list uses excerpt-only and shows state chips
- skeleton loading exists
- triage status is visible and clear

---

## Accessibility + Motion
### UI-08 — Accessibility contract pass: focus, escape-stack, drag alternatives
Labels: `Priority III`, `frontend`, `a11y`, `ux`
Depends: UI-02
AC:
- focus restore on modal close
- focus never obscured behind overlays (2.4.11)
- keyboard-only scripts pass

### UI-09 — Motion tokens + reduced-motion support
Labels: `Priority III`, `frontend`, `ux`
Depends: UI-01
AC:
- motion tokens exist
- reduced-motion respected
- overlays animate consistently (150–250ms range)
Reference:
- Material duration guidance: https://m1.material.io/motion/duration-easing.html

---

## Quality Gates
### UI-10 — Add ESLint + formatting + CI lane
Labels: `Priority II`, `frontend`, `hardening`
AC:
- eslint config and scripts exist
- CI lane added and required

### UI-11 — Add visual regression tests (Playwright screenshots)
Labels: `Priority III`, `testing`, `frontend`
AC:
- 3 stable screenshot tests added using `toHaveScreenshot`
Reference:
- Playwright screenshot assertions: https://playwright.dev/docs/test-snapshots

### UI-12 — Optional: Storybook for primitives + Autodocs
Labels: `Priority IV`, `frontend`, `ux`, `docs`
AC:
- storybook configured for vue3-vite
- primitives have stories
Reference:
- https://storybook.js.org/docs/get-started/frameworks/vue3-vite
