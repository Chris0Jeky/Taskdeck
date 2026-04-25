# PAPER-05 · Board / Kanban surface in Paper

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02, PAPER-03.

## Goal

Re-skin `BoardView` as the Paper kanban — 4 columns, index card variant, hairline density.

## Spec
- Columns: 280px wide, `--paper-2` background, `1px var(--line-soft)` border, 16px gap.
- Header: mono serial (`§ 04`) + serif name + count badge.
- Card variant A — Index card (default):
  - Top: mono serial number (`C-090`).
  - Title serif 14.5px medium.
  - Optional 1-line body.
  - 11px mono metadata bottom strip.
- Card variant B — Tag ribbon: 4px colored ribbon left edge keyed to label.
- Column footer: hairline `+ capture` button.
- WIP-limit warning surfaces as overdue tagstamp on column header.

## Implementation
- Create `views/paper/PaperBoardView.vue`, `PaperBoardColumn.vue`, `PaperBoardCard.vue`.
- Reuse existing drag-drop composable + `boardStore` actions.
- Hover lifts card 1px via `box-shadow` (no scale).
- Card max-width 248px inside column; body line-clamp 1.

## Tests
- vitest: variant A by default; variant B with prop.
- Playwright: drag a card across columns; assert audit-log entry.
- Accessibility: 2px ember focus ring with 1px gap.

## Adversarial review
- [ ] No re-render storm on drag.
- [ ] 200-card columns scroll smoothly (60fps).
- [ ] Empty column shows hairline placeholder.
- [ ] Done column heuristic still applies for metrics.
