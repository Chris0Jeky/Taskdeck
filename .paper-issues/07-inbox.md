# PAPER-07 · Inbox / Capture in Paper

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02, PAPER-03. **Recommended:** PAPER-10 for nib variant.

## Goal
Re-skin `InboxView` (and standalone capture) with two Paper variants.

## Spec
### Variant A — Single-line nib
- Focus-mode capture; single giant italic-serif input centered (44–64px).
- After Enter, haiku structures the capture into title + tags via Ink Bleed motion (PAPER-10).
- Bound by `⌘;`.

### Variant B — Composer ledger (default)
- Multi-line composer (Inter 14.5px, paper-card, hairline border) with metadata sidebar:
  - Board picker, label picker, due date, attachments drop zone.
- Triage table below: 11 captured items with auto-suggested cards, tagstamps, accept/reject hairline buttons.

## Implementation
- Create `views/paper/PaperInboxView.vue` orchestrator.
- `PaperCaptureNib.vue`, `PaperCaptureComposer.vue`, `PaperTriageTable.vue`.
- Reuse `captureStore` and triage composable.
- ⌘; toggles between variants.

## Tests
- vitest: triage table emits correct store actions.
- vitest: nib focuses on mount; submit on Enter; Shift+Enter newline.
- Playwright: capture → triage → proposal smoke.

## Adversarial review
- [ ] Long capture text wraps at 80ch in nib.
- [ ] Pasted multi-line preserved.
- [ ] Triage actions idempotent under double-click.
- [ ] Ink-bleed only fires when LLM call begins; otherwise static dried state.
