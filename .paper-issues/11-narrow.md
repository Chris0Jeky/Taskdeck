# PAPER-11 · Narrow companions — 375 phone + 768 tablet

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-03 + at least one surface (Board or Review).

## Goal
Match the narrow specifications so Paper survives at phone (375) and tablet (768) widths.

## Spec

### Phone (≤480px, target 375)
- Single-column.
- Sidebar collapses to bottom tab bar with 4 letterform glyphs (H · T · R · I).
- Stamps shrink to 48px.
- Type scales down 10% via `--paper-scale: .9`.

### Tablet (≤1024px, target 768)
- Sidebar collapses to icon-only rail (60px).
- Boards reduce to 2 visible columns with horizontal scroll, snap points.

## Implementation
- Narrow-mode CSS in `paper-tokens.css` under media queries.
- `PaperSidebar.vue` reads viewport and renders `rail` / `bottombar` variant.
- `PaperBoardView.vue` adds horizontal snap scroll at tablet width.
- Type scale via `--paper-scale` consumed by `.tk-h1/h2/h3/lede/body`.

## Tests
- Playwright: `page.setViewportSize` 375/768; non-empty + no console errors.
- vitest: PaperSidebar renders bottom-bar variant when matchMedia simulates phone.

## Adversarial review
- [ ] No fixed widths leak from desktop CSS.
- [ ] Bottom-bar respects iOS safe area inset.
- [ ] Type scale doesn't cascade twice (no compounding 0.9 × 0.9).
- [ ] Tablet rail still respects active-route ember accent.
