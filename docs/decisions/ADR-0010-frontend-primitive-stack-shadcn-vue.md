# ADR-0010: Frontend Primitive Stack — shadcn-vue

- **Status**: Accepted
- **Date**: 2026-03-28
- **Deciders**: Project maintainers

## Context

Taskdeck's frontend had grown ad-hoc: hand-rolled modals, inline button styling, inconsistent keyboard handling. This broke the keyboard-first interaction model and made accessibility compliance difficult. A unified component primitive layer was needed.

## Decision

Adopt **shadcn-vue** (copy-paste ownership model built on Reka UI primitives):

- Provides 50+ pre-composed accessible components (Button, Dialog, DropdownMenu, Tabs, etc.)
- WAI-ARIA keyboard excellence out of the box (roving tabindex, focus trapping, typeahead)
- Copy-paste model: components live in the project, not in `node_modules`; full ownership for customization
- Components remap to `--td-*` design tokens for theme coherence

## Alternatives Considered

- **Reka UI alone (headless)**: Correct accessibility primitives but zero styling — requires building every component from scratch; too slow for current velocity.
- **Headless UI (Tailwind Labs)**: Excellent quality but Vue 3 support lags; smaller component set; less active Vue community.
- **PrimeVue / Vuetify**: Full component libraries but opinionated styling conflicts with custom design system; harder to override; heavier bundle.
- **Build everything from scratch**: Maximum control but highest time cost; keyboard/ARIA compliance is hard to get right manually.

## Consequences

- **Positive**: Fast primitive delivery; accessibility built-in; components own their styling and can be freely modified; Reka UI foundation is well-maintained.
- **Negative**: Copy-paste model means manual updates when upstream fixes land (but low frequency); some components need significant restyling to match Obsidian/Ember theme.
- **Neutral**: Existing hand-rolled components can be migrated incrementally.

## References

- `docs/analysis/ui-primitive-stack-decision-spike.md` — full comparison
- UI-00 wave tracker: `#242`
