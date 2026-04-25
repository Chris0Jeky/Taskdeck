# Paper at Night — color audit

Tracking issue: #1008 · Master tracker: #996

## Scope

This audit covers files that ship inside the Paper scope (`.paper` /
`.paper-night`):

- `frontend/taskdeck-web/src/paper-tokens.css`
- `frontend/taskdeck-web/src/components/paper/**`
- `frontend/taskdeck-web/src/views/paper/**` (after surface PRs merge)
- `frontend/taskdeck-web/src/components/shell/AppShell.vue` (paper-mode block)

The Paper night theme inverts every token under the `.paper-night` class.
Anything that hard-codes a hex literal inside the Paper scope short-circuits
the inversion and ships visibly broken in dark mode.

## Audit method

```sh
grep -RnE "#[0-9a-fA-F]{6}|#[0-9a-fA-F]{3}\b" \
  frontend/taskdeck-web/src/paper-tokens.css \
  frontend/taskdeck-web/src/components/paper \
  frontend/taskdeck-web/src/views/PaperStyleGuideView.vue \
  frontend/taskdeck-web/src/components/shell/AppShell.vue
```

## Findings — foundation + shell scope (PAPER-01..03)

| File | Status |
|---|---|
| `paper-tokens.css` | Pass. All hex literals are token declarations (`.paper { --ember: #a8421f; }` and `.paper-night { --ember: #d96a3e; }`) or shadow constants intentionally hand-tuned per theme. |
| `components/paper/Paper*.vue` | Pass. No hex literals; every color reference uses `var(--*)`. |
| `views/PaperStyleGuideView.vue` toolbar (lines 341-397, inside `<style scoped>`) | **Intentional exception.** The toolbar wraps the preview frame and lives OUTSIDE `.paper` / `.paper-night`, so it cannot use Paper tokens. It uses `--td-*` Obsidian tokens with hex fallbacks for environments where Obsidian tokens are unset. The `#a8421f` ember literals on lines 380-382 (the `aria-pressed=true` style) are a deliberate duplicate of `--ember`; future toolbar UI changes should preserve this exception. |
| `components/shell/AppShell.vue` paper-mode block | Pass. Delegates to `PaperSidebar` / `PaperTopBar` for paper-scoped rendering. |

## Follow-ups for surface scope (PAPER-04..09)

When PRs #1013, #1014, #1025, #1026, #1027, #1028 merge, re-run the audit
across `frontend/taskdeck-web/src/views/paper/**`. Per-surface follow-up
issues will be filed referencing #1008.

## Lint enforcement

To prevent regressions, future work should add either:

- A project-local ESLint rule in `frontend/taskdeck-web/eslint-rules/no-paper-hex.js`
  that flags hex literals inside `frontend/taskdeck-web/src/components/paper/**`
  and `frontend/taskdeck-web/src/views/paper/**`, OR
- A CI grep step in `.github/workflows/paper-color-audit.yml` that runs the
  audit command above and fails the build on any new hex literal in scope.

The lint rule is preferable (faster feedback to authors) but the CI grep is
acceptable as a low-effort fallback. This work is deferred to a follow-up
issue once the lint scope is broader (i.e. surfaces have merged and we have
a stable hit-list of intentional exceptions to allow-list).
