# PAPER-12 · Paper at Night — dark theme verification across all surfaces

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01 .. PAPER-09.

## Goal
Verify and finalize Paper at Night (`.paper-night`) across every surface from earlier slices. Visual parity with light, inverted substrate, ember warming to `#d96a3e`, sage applied to `#8aae72`.

## Scope
- Add Playwright run visiting every Paper surface in both modes; assert no console errors and no blue browser default focus rings.
- Spot-check each surface against README dark section: substrate `--paper` `#14110d` with fiber pattern + screen blend; stamps gain subtle glow; confidence dial / cadence bars use warmer dark variants.
- Fix any leaked light-mode hex literals.
- Ensure `prefers-color-scheme: dark` only auto-selects night when mode is `auto`.

## Implementation
- Add `frontend/taskdeck-web/tests/e2e/paper-night.spec.ts`.
- Audit pass: grep for `#[0-9a-fA-F]{6}` under `components/paper/` and `views/paper/`; replace literals with token references.
- Add paper-night story to every primitive's Storybook entry.

## Tests
- Playwright dark suite, ≥ 1 assertion per surface.
- vitest: `paperThemeStore.mode = 'auto'` toggles night on `prefers-color-scheme: dark`.

## Adversarial review
- [ ] No surface has hard-coded color violating night palette.
- [ ] Ember stays at `#d96a3e` in night, not day `#a8421f`.
- [ ] Letterpress text shadows inverted (light atop dark) on night.
- [ ] No surface requires manual toggle to be legible (auto mode works).
