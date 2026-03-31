# ADR-0011: Design Token System — Obsidian & Ember Theme

- **Status**: Accepted
- **Date**: 2026-02-23
- **Deciders**: Project maintainers

## Context

The frontend used hardcoded Tailwind color classes and scattered `rgba()` values. This prevented cohesive theming, made dark/light mode switching impossible, and created visual inconsistencies as different contributors made different color choices.

## Decision

Implement a CSS custom property system (`--td-*` namespace) with:

- **Obsidian surface tiers**: 7-tier dark surface scale from `#0e0e0e` to `#3a3939`
- **Ember accent colors**: Primary `#ffb3ae`, ember `#ff4d4d`, ember-glow `#ff5352`
- **Argent text hierarchy**: 4-tier text scale (primary, secondary, tertiary, muted)
- **Semantic tokens**: success, warning, error, info with light/dark variants
- **Glass effects**: Backdrop blur with semi-transparent backgrounds
- **Light theme override**: Full `[data-theme="light"]` variant with inverted surfaces

All shell and board surfaces must use tokens, not hardcoded colors. Tailwind utility classes reference token aliases.

## Alternatives Considered

- **Tailwind theme config only**: Limited to Tailwind contexts; doesn't work in scoped CSS or non-Tailwind components.
- **CSS Modules with color constants**: No runtime theme switching; requires build-time compilation.
- **Design token JSON (Style Dictionary)**: Over-engineered for current scale; adds build step.

## Consequences

- **Positive**: Theme-consistent UI across all surfaces; dark/light switching works globally; new components inherit the right colors automatically; accessibility contrast ratios checkable per-token.
- **Negative**: Migration cost — existing hardcoded colors must be replaced (see `#612` Starter Pack modal still uses hardcoded Tailwind).
- **Neutral**: Scrollbar, focus ring, and shadow tokens provide micro-level consistency.

## References

- `frontend/taskdeck-web/src/design-tokens.css` — token definitions
- `docs/analysis/2026-02-23_frontend-premium-ui-synthesis.md` — UI strategy
- UI-00 wave tracker: `#242`
