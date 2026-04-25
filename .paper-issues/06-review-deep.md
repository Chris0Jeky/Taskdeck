# PAPER-06 · Review (Deep) — central surface

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02, PAPER-03. **Recommended:** PAPER-10.

## Goal

Replace `ReviewView` with the Paper deep-Review surface — the most important screen.

## Spec (paper/surface-review-deep.jsx + README §3)

3-column grid `280px | flex | 320px`:

### Queue rail
- Eyebrow `Queue · N awaiting · M stale` + filter pills.
- `Q` rows with serial, age, serif title, author + confidence + reach.
- Active item: 2px ember left border + ember-bloom gradient.
- "Recently applied · undoable" with `↶ time-left`.
- "This week" mini cadence (7 bars) + apply/undo rates.

### Main
- Header: PROPOSED tagstamp, serial, serif italic 36–44px title with `<em>` highlights, lede, 200px confidence dial card right-aligned.
- Sticky decision rail: tagstamp DECISION + summary mono + Reject/Edit/Defer/Apply (ember).
- §I The change: 2-col before/after card. Field diff strip below.
- §II Provenance: 5 rows (primary/contextual/excluded/inferred), 32px icon + 200px italic key + value.
- §III Side effects: 7-row table + Reversibility card with `PaperUndoTimeline`.
- §IV Conflicts & warnings (warn rust / info mute / ok sage).
- §V History: ledger table, serial + event + age + status pill.

### Right rail
- Author card with proposed Stamp absolutely positioned top-right rotated −9°; confidence breakdown bars.
- Why-now card.
- Similar past decisions (3) with verdict tagstamps.
- Decide-with-keys card in ember tint.

## Keyboard
⏎ Apply · ⌫ Reject · E Edit · D Defer 1h · P Provenance · Space Preview

## Implementation
- Create `views/paper/PaperReviewView.vue` orchestrator.
- Decompose into `ReviewQueueRail.vue`, `ReviewMain.vue`, `ReviewRightRail.vue`, plus subcomponents.
- Reuse `useReviewProposals`; extend with provenance/sideEffects/confidenceBreakdown/conflicts/history/similarPast selectors.
- If backend lacks fields, ship feature-flagged stubs with mock data and open follow-up issues per gap.
- `useReviewKeymap` composable scoped to route.

## Backend gaps to file as sub-issues
- Provenance fullReadSet link target.
- SimilarPastDecisions query (apply rate windowed).
- ConfidenceBreakdown 4-component.
- Reversibility per-proposal (defaults to 6h).

## Tests
- vitest: confidence dial; status pill mapping; keyboard map dispatches.
- Playwright: open Review; press ⏎; assert applied stamp + undo timer.

## Adversarial review
- [ ] Decision rail stays sticky without overlap (z-index 2).
- [ ] Apply remains atomic — single ledger entry post-apply.
- [ ] ⏎ inside text inputs (defer reason, edit composer) does NOT apply.
- [ ] Erase-line on Reversibility animates linearly over configured window.
- [ ] No ember leak into conflicts table (warn = `--overdue`).
- [ ] Stamp on author card doesn't intercept clicks.
