# PAPER-06 · Review (Deep) — central surface

> **Partially superseded by `#1298` (REVIVAL-02, 2026-07-17):** the undo affordances specced below
> ("Recently applied · undoable" with `↶ time-left`, apply/undo rates, and the Reversibility card's
> `PaperUndoTimeline`) were built and then **removed** — no revert endpoint exists. The shipped surface
> shows a factual recency ledger ("Recently applied", age only) and an "Apply considerations" apply-risk
> card instead. Do not rebuild the undo copy from this spec.

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02, PAPER-03. **Recommended:** PAPER-10.

## Goal

Replace `ReviewView` with the Paper deep-Review surface — the most important screen.

## Spec (paper/surface-review-deep.jsx + README §3)

3-column grid `280px | flex | 320px`:

### Queue rail
- Eyebrow `Queue · N awaiting · M stale` + filter pills.
- `Q` rows with serial, age, serif title, author + confidence + reach.
- Active item: 2px ember left border + ember-bloom gradient.
- ~~"Recently applied · undoable" with `↶ time-left`.~~ *[superseded by `#1298`: now a factual "Recently applied" recency ledger]*
- "This week" mini cadence (7 bars) ~~+ apply/undo rates~~ *[undo rate removed by `#1298`]*.

### Main
- Header: PROPOSED tagstamp, serial, serif italic 36–44px title with `<em>` highlights, lede, 200px confidence dial card right-aligned.
- Sticky decision rail: tagstamp DECISION + summary mono + Reject/Edit/Defer/Apply (ember).
- §I The change: 2-col before/after card. Field diff strip below.
- §II Provenance: 5 rows (primary/contextual/excluded/inferred), 32px icon + 200px italic key + value.
- §III Side effects: 7-row table + ~~Reversibility card with `PaperUndoTimeline`~~ *[superseded by `#1298`: shipped as an "Apply considerations" apply-risk card, no timeline]*.
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
