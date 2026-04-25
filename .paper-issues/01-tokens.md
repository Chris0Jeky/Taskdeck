# PAPER-01 · Foundation — Paper tokens, theme classes, fonts, substrate

Part of the **Paper & Graphite, Ember Edition** overhaul (master tracker: PAPER-00).

## Goal

Land the foundation that every later slice depends on: Paper tokens, theme scope, type utilities, paper-substrate background, and font loading. **Nothing else can ship before this.**

## Scope

### CSS / tokens
- Add `frontend/taskdeck-web/src/paper-tokens.css` mirroring `design_handoff_taskdeck_paper/paper/tokens.css` exactly:
  - `:root, .paper { … }` light defaults
  - `.paper-night { … }` strict dark mirror
  - Paper substrate: `repeating-linear-gradient` long-fiber + SVG fleck noise via `background-blend-mode: multiply`
  - All typography utility classes: `.tk-display`, `.tk-h1`, `.tk-h2`, `.tk-h3`, `.tk-lede`, `.tk-body`, `.tk-meta`, `.tk-eyebrow`, `.tk-num`, `.tk-serial`, `.tk-ink-italic`
  - `.hr-line`, `.hr-soft`, `.hr-double`, `.rule-ledger`
  - `.stamp` / `.stamp.ember` / `.stamp.applied` / `.stamp b` / `.stamp .stamp-num`
  - `.letterpress`, `.tagstamp`
  - `.card`, `.card-lift`, `.well`, `.surface`, `.halo-ember`
  - `.btn`, `.btn-primary`, `.btn-ember`, `.btn-ghost`, `.kbd`, `.kbd-light`
  - `.hl-icon` / `-md` / `-lg`
  - `.status` family (proposed/applied/overdue/draft/live + pulse keyframes)
  - `.diff-add`, `.diff-rem`, `.erase-line`
- Import the new sheet from `main.ts` after `design-tokens.css` so Paper sits parallel, not replacing Obsidian.
- Self-host Fraunces, Inter, JetBrains Mono via `@font-face` with `font-display: swap`. Critical weights: Fraunces italic 400 + 500, Inter 400/500/600, JetBrains Mono 500.

### Theme scoping
- Add `frontend/taskdeck-web/src/store/paperThemeStore.ts` Pinia store: `mode: 'paper' | 'paper-night' | 'auto' | 'off'` (default `off`).
- App root applies `paper`/`paper-night` class to `<body>` when mode is on. Persists to `localStorage` key `td.paper.mode`.
- Honor `prefers-color-scheme: dark` only when mode is `auto`.

### Style-guide route
- Add `frontend/taskdeck-web/src/views/PaperStyleGuideView.vue` route at `/styleguide/paper`.
- Render every type utility, component class, status pill, diff strip, stamp variant, both themes side-by-side.

### Tests
- vitest: store mode persistence and body-class application.
- vitest: snapshot `.tk-display em`, `.stamp.ember`, `.tagstamp` for token drift.

## Files to touch / create

- create: `frontend/taskdeck-web/src/paper-tokens.css`
- create: `frontend/taskdeck-web/src/store/paperThemeStore.ts`
- create: `frontend/taskdeck-web/src/views/PaperStyleGuideView.vue`
- create: `frontend/taskdeck-web/public/fonts/*.woff2`
- modify: `frontend/taskdeck-web/src/main.ts`
- modify: `frontend/taskdeck-web/src/App.vue`
- modify: `frontend/taskdeck-web/src/router/index.ts`

## Adversarial review checklist

- [ ] Paper variables scoped under `.paper`/`.paper-night` (no leak when mode is off).
- [ ] No `--td-*` token renamed or removed.
- [ ] Fraunces italic loads via `font-display: swap` — no FOUC.
- [ ] `.tk-display em` renders in `--ember`.
- [ ] Background substrate respects `prefers-reduced-transparency` and zoom 200%.
- [ ] `.paper-night` does not require a `data-theme` attribute swap — class only.

## Acceptance

- `npm run dev` shows `/styleguide/paper` with all type, components, both themes.
- `npm run typecheck` and `npm run lint` clean.
- Master tracker referenced.
