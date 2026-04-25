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

A correctly-anchored regex matches `#abc` (3-digit RGB), `#abcd`
(4-digit RGBA), `#aabbcc` (6-digit RGB), and `#aabbccdd` (8-digit RGBA)
forms with a trailing `\b` word boundary so partial matches like
`#abcdefg` cannot slip through.  Note: `\b` checks word-character
boundaries (`[a-zA-Z0-9_]`), so `#abc` followed by a non-hex letter like
`g` will NOT match (because `g` is still a word character).  This is
acceptable — CSS hex colors are always followed by whitespace, `;`, `,`,
or `)`, all of which are non-word characters that trigger `\b` correctly
for both the 3/4-digit and 6/8-digit branches.

```sh
grep -RnE '#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6,8})\b' \
  frontend/taskdeck-web/src/paper-tokens.css \
  frontend/taskdeck-web/src/components/paper \
  frontend/taskdeck-web/src/views/PaperStyleGuideView.vue \
  frontend/taskdeck-web/src/components/shell/AppShell.vue
```

The CI workflow at `.github/workflows/reusable-paper-color-audit.yml`
enforces the same regex.  For the `paper-tokens.css` baseline count, the
workflow uses `grep -oE … | wc -l` (occurrence-based, not line-based) and
filters out CSS comment lines (`^\s*(\*|/\*)`) to avoid false positives
from issue references and illustrative values in comments.  The AppShell
scan is scoped to the paper-mode region only (`<Paper*>` invocations,
`td-shell--paper` class refs, and `paperTheme.isOn` v-if guards) so hex
literals in the unrelated Obsidian shell code path do not block CI.

The regex by itself does not cover Vue `<template>` `style="..."` inline
attributes — those are caught by a second invocation of the same pattern
restricted to `.vue` files. As of this audit, no `*.vue` file under
`components/paper/` carries an inline-style hex literal.

## Findings — foundation + shell scope (PAPER-01..03)

| File | Status |
|---|---|
| `paper-tokens.css` token-declaration block (`.paper { ... }` lines 15-89, `.paper-night { ... }` lines 91-128) | **Pass — token declarations.** Every hex literal in these blocks is the declaration site of a `--token` and inverts cleanly between light and night. See "Token declarations" below for the full list. |
| `paper-tokens.css` shadow & substrate non-token literals | **Pass — per-theme hand-tuned constants.** See "Allow-listed shadow / substrate literals" below for the explicit list. These literals are inside `.paper { ... }` or `.paper-night { ... }` selectors that already invert per theme. |
| `paper-tokens.css` shared (theme-agnostic) literals | **Pass after PAPER-12 fix-ups.** The `.halo-ember` and `.pbtn-primary:hover` rules previously short-circuited the inversion (`#a8421f30` and `#000` respectively, applied to BOTH selectors). They now use tokens / split per-theme — see "PAPER-12 fix-ups" below. Remaining hardcoded hex in component states: `.pbtn-primary:hover` (`#000` ×2 in `.paper`-scoped rule), `.pbtn-ember` (`#fbf2e8` text, `#ffffff20` inset highlight) — these are intentional per-theme constants documented in the allow-list. |
| `paper-tokens.css` letterpress shadows (lines 269-270) | **Pass — inverted per theme.** `.paper .letterpress` uses `0 1px 0 #ffffffa0, 0 -.5px 0 #1a18141a` (light highlight on top, dark shadow below — appropriate for cream substrate). `.paper-night .letterpress` uses `0 1px 0 #00000080, 0 -.5px 0 #ffffff10` (dark highlight on top, light shadow below — appropriate for dark substrate). The shadows are correctly inverted so engraved type reads on both substrates. |
| `components/paper/Paper*.vue` | **Pass.** No hex literals in `<template>` (inline `style=""`), `<script>`, or `<style>` blocks. Every color reference uses `var(--*)`. |
| `views/PaperStyleGuideView.vue` toolbar (lines 341-397, inside `<style scoped>`) | **Intentional exception.** The toolbar wraps the preview frame and lives OUTSIDE `.paper` / `.paper-night`, so it cannot use Paper tokens. It uses `--td-*` Obsidian tokens with hex fallbacks for environments where Obsidian tokens are unset. The `#a8421f` ember literals on lines 380-382 (the `aria-pressed=true` style) are a deliberate duplicate of `--ember`; future toolbar UI changes should preserve this exception. |
| `components/shell/AppShell.vue` paper-mode block | **Pass.** No hex literals. Delegates to `PaperSidebar` / `PaperTopBar` for paper-scoped rendering. |

## Token declarations (allow-listed)

These hex literals are the declaration sites of design tokens. They must
appear once per theme block and are NOT regressions:

- `.paper { --paper, --paper-2, --paper-card, --paper-edge, --line, --line-soft, --rule, --ink, --ink-deep, --ink-2, --mute, --faint, --whisper, --ember, --ember-deep, --ember-bloom, --ember-tint, --ember-ink, --applied, --applied-tint, --overdue, --overdue-tint, --awaits }` — lines 17-45.
- `.paper-night { --paper, --paper-2, --paper-card, --paper-edge, --line, --line-soft, --rule, --ink, --ink-deep, --ink-2, --mute, --faint, --whisper, --ember, --ember-deep, --ember-bloom, --ember-tint, --ember-ink, --applied, --applied-tint, --overdue, --overdue-tint }` — lines 92-116.

## Allow-listed shadow / substrate literals

Inside `.paper { ... }` / `.paper-night { ... }` selectors, these are
hand-tuned per-theme constants that already invert correctly because the
SELECTOR itself is per-theme. They are intentionally not converted to
tokens because they are alpha-blended overlays (shadow stacks, paper-fiber
noise patterns) rather than primary palette colors.

| Theme | Line | Literal | Used in |
|---|---|---|---|
| `.paper` | 23 | `#1a181410` | `--rule` (subtle horizontal rule alpha) |
| `.paper` | 72-75 | `#1a18140d`, `#1a18140f`, `#1a181430`, `#1a181410` | `--shadow-card`, `--shadow-lift`, `--shadow-press` (ink-deep alpha shadows) |
| `.paper` | 138 | `#1a181408` | substrate fiber stripe (ink-deep alpha) |
| `.paper-night` | 98 | `#ffffff0a` | `--rule` (subtle horizontal rule alpha) |
| `.paper-night` | 122-125 | `#00000060`, `#ffffff04`, `#00000080`, `#ffffff05`, `#00000040` | `--shadow-card`, `--shadow-lift`, `--shadow-stamp` adjacent, `--shadow-press` (paired black/white alpha overlays) |
| `.paper-night` | 148 | `#ffffff05` | substrate fiber stripe (white alpha) |
| `.paper` | 272 | `#ffffffa0`, `#1a18141a` | `.letterpress` shadow stack (light highlight, dark depression) |
| `.paper-night` | 273 | `#00000080`, `#ffffff10` | `.letterpress` shadow stack (inverted: dark highlight, light depression) |
| `.paper` | 351 | `#000` (×2) | `.pbtn-primary:hover` background + border-color. Pure black is barely darker than `--ink-deep` (`#0a0908`) so the hover shift is subtle. This rule is `.paper`-scoped; the `.paper-night` hover uses `var(--ink-2)` (see PAPER-12 fix-ups). |
| `.paper` / `.paper-night` | 356-357 | `#fbf2e8`, `#ffffff20` | `.pbtn-ember` shared rule. `#fbf2e8` is `--ember-ink`-equivalent paper-card text on ember; `#ffffff20` is the inset highlight. Both are ember-button decorations and read correctly on the ember background in BOTH themes (the ember background is dark-orange in both). |

## PAPER-12 fix-ups (this PR)

Two rules previously hard-coded light-mode literals into a `.paper, .paper-night` shared selector, short-circuiting the inversion:

1. `.halo-ember` (line ~305) — last shadow stop changed from `0 8px 24px -10px #a8421f30` to `0 8px 24px -10px var(--ember-bloom)` so the outer halo glow inverts to night ember.
2. `.pbtn-primary:hover` (line ~331) — split into per-theme rules. `.paper` retains `background: #000; border-color: #000` (slight darken on near-black). `.paper-night` uses `background: var(--ink-2); border-color: var(--ink-2)` (slight darken on light cream — `#fbf3df → #c8bfa9`), which preserves the "press a little harder into the ink" intent on the dark substrate.

## Follow-ups for surface scope (PAPER-04..09)

When PRs #1013, #1014, #1025, #1026, #1027, #1028 merge, re-run the audit
across `frontend/taskdeck-web/src/views/paper/**`. Per-surface follow-up
issues will be filed referencing #1008.

## CI baseline — `paper-tokens.css` (68 hex occurrences)

The CI workflow counts individual hex occurrences (not matching lines) in
non-comment lines of `paper-tokens.css`.  The breakdown:

| Category | Count |
|---|---|
| Token declarations (`.paper` block, lines 16-44) | 23 |
| Shadow constants (`.paper` block, lines 71-74) | 4 |
| Token declarations (`.paper-night` block, lines 91-116) | 22 |
| Shadow constants (`.paper-night` block, lines 121-124) | 5 |
| Substrate fiber texture (`.paper`, lines 137-140) | 3 |
| Substrate fiber texture (`.paper-night`, lines 149-152) | 3 |
| Letterpress shadows (`.paper` + `.paper-night`, lines 272-273) | 4 |
| `.pbtn-primary:hover` (`.paper`-scoped, `#000` ×2, line 351) | 2 |
| `.pbtn-ember` (`#fbf2e8` + `#ffffff20`, lines 356-357) | 2 |
| Total | **68** |

When adding new hex literals, bump the baseline in
`reusable-paper-color-audit.yml` and add the rationale to this table in
the same PR.

## Lint enforcement

This PR ships the **CI grep step** at
`.github/workflows/reusable-paper-color-audit.yml`, called from
`ci-required.yml`, which runs the audit regex above and fails the build on
any hex literal that is not on the allow-list. The allow-list lives in the
workflow file as a hand-maintained set of `(file, line)` pairs.

A project-local ESLint rule (`frontend/taskdeck-web/eslint-rules/no-paper-hex.js`)
would give faster authoring feedback and is filed as a follow-up — the CI
grep covers regressions, the ESLint rule is purely a developer-experience
improvement.
