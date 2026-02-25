# Design System: Tokens, Themes, Density, Typography
Date: 2026-02-23
Status: Draft

Taskdeck already has a `src/design-tokens.css` file. This is good. The next step is to:
1) formalize token namespaces
2) support light/dark + density modes
3) standardize how components consume tokens (no raw hex/rgba)

---

## Token architecture (recommended)
Keep tokens as CSS variables, but split into layers:

### Layer A — Base tokens (raw)
- gray scale steps
- accent scale steps
- size scales (spacing, radius)
- typography scale
- motion primitives

### Layer B — Semantic tokens (mapped)
- surface colors
- text colors
- borders
- focus rings
- shadows
- danger/warn/success

### Layer C — Component tokens (optional later)
- button height, padding, border
- input height, border
- dialog padding

### Why this helps
- base tokens remain stable
- semantic tokens can change for themes
- components stay consistent and avoid hard-coded colors

---

## Theme strategy (light + dark)
Recommended mechanism:
- default theme on `:root`
- override with `[data-theme="dark"]` or `.dark`

Example:
```css
:root {
  --td-surface-canvas: #f8fafc;
  --td-surface-panel: #ffffff;
  --td-text: #0f172a;
}

[data-theme="dark"] {
  --td-surface-canvas: #0b1220;
  --td-surface-panel: #0f172a;
  --td-text: #e2e8f0;
}
```

Add:
- `prefers-color-scheme` defaulting:
```css
@media (prefers-color-scheme: dark) {
  :root:not([data-theme]) { /* optional */ }
}
```

---

## Density strategy (“comfortable” vs “compact”)
You already hinted at density via attributes previously. Standardize it:

- `[data-density="comfortable"]` (default)
- `[data-density="compact"]`

Define component-level spacing adjustments (input heights, paddings, list row heights).
This keeps the UI premium for both:
- “lots of information” workflows
- “calm and spacious” workflows

---

## Typography
Premium feel typically comes from:
- consistent type scale
- consistent line heights
- consistent weights
- a single sans font family (Inter-like) plus mono for ids/logs

Keep the typography scale in tokens:
- `--td-font-xs...`
- add line heights: `--td-leading-tight/normal/relaxed`

---

## Tailwind integration
You can bind Tailwind utilities to tokens.

Tailwind’s theme variables documentation is a good reference for how tokens map to utilities.  
https://tailwindcss.com/docs/theme

If you remain on Tailwind 3.x:
- create component classes in `@layer components` using `@apply`, but reference CSS vars for colors.
- avoid using `bg-gray-*` directly in new components.

---

## Migration rules (to prevent design drift)
1) No new raw hex colors in SFC styles.
2) No new `bg-gray-*` in templates (new work uses semantic tokens).
3) Existing styles can stay until touched (incremental migration).

Add a lint rule later (Stylelint or custom) if drift becomes a problem.

---

## Deliverable for UI wave
- `src/design-tokens.css` expanded with:
  - semantic tokens for surfaces/text/borders
  - dark theme overrides
  - density overrides
  - motion tokens (durations/easing)
