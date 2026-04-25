# PAPER-09 · Misc surfaces — Card detail, ⌘K palette, Shortcuts overlay, Toasts, Empty states

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02, PAPER-03.

## Sub-surfaces

### Card detail (focus mode)
- Wide single-column, max-width 720px on `--paper-2` page.
- Title serif 28px, body 15px Inter, subtasks `.rule-ledger` checklist.
- Activity log on the right as vertical ledger.
- Pending proposal banner at top if a proposal exists.

### ⌘K Command palette
- 640px wide, centered, `--paper-card`, 4px radius.
- 13px Inter input, no border, 16px padding.
- Result rows: 40px, hairline icon, label, mono kbd hint.
- AI-action rows have ember dot + "haiku" mono label.

### Shortcuts overlay
- 3-column reference: Navigate / Capture & Review / Boards.
- Each row: kbd pill + label + mono note.
- Triggered by `?`.

### Toast stack
- Bottom-right, 320px wide, 56px height, paper-card with hairline border.
- Tagstamp left, 13px message, "undo" hairline link with countdown right.

### Empty states
- Centered serif italic copy, no illustrations, single hairline CTA.
- Ember-tint backgrounds only on "act on this" empty states.

## Implementation
- `PaperCardDetailView.vue`, `PaperSubtaskLedger.vue`, `PaperCardActivity.vue`.
- `PaperCommandPalette.vue` (replaces shell-level palette in paper mode).
- `PaperShortcutsOverlay.vue`.
- `PaperToastContainer.vue`.
- `PaperEmptyState.vue` generic.

## Tests
- vitest: each surface renders core state; palette filters; shortcuts shows all groups.
- Playwright: ⌘K open + select; `?` overlay open.

## Adversarial review
- [ ] Toast countdown paused on hover.
- [ ] Palette uses arrow keys + Enter; Escape closes.
- [ ] Empty state CTAs are real actions.
- [ ] Card detail preserves scroll position when navigating between cards.
