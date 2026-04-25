# PAPER-08 · Today / End-of-day dossier

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02, PAPER-03.

## Goal
Replace `TodayView` with the Paper end-of-day dossier — the closing read of the workday.

## Spec (paper/surface-today.jsx + README §10)

1. **Cover** — paper-2→paper gradient, 56px serif italic headline ("Today, you moved nine cards."), tk-lede paragraph, "Seal day" CTA (ember), dossier serial top-right.
2. **5 stat cards** — cards moved · proposals applied · captures triaged · longest focus · overdue. 38px serif italic number, mono eyebrow, sub-line, 2px top accent in tone.
3. **§I Cadence** — 24-hour activity strip (24 bars, peak hour ember) + first/peak/last mini-stats.
4. **§II Ledger** — full event log of the day in `.rule-ledger`: serial `L-NNN`, time, who pill, what, dot tone.
5. **§III Decisions** — 4-up grid of today's proposals with verdict tagstamps (APPLIED green, REJECTED rust, DEFERRED ember).
6. **§IV Boards touched** — list of boards with move counts and proposal pills. Untouched boards dimmed.
7. **§V Carry-over** — overdue cards in rust-bordered card. "Pin to tomorrow's morning" CTA.
8. **§VI Streak** — 90-day grid, 30 cols, ember intensity per day, today highlighted.
9. **§VII A line for tomorrow** — italic serif textarea, autosaved.

## Backend gaps (file as sub-issues)
- Daily cadence aggregation per-hour buckets.
- Streak query (last 90 days, sealed flag per day).
- "Seal day" snapshot action.
- "A line for tomorrow" per-day storage.

## Implementation
- Create `views/paper/PaperTodayView.vue` + section subcomponents.
- Cadence and streak as plain SVG (no chart library).
- "Seal day" CTA dispatches `todayStore.sealDay()`.

## Tests
- vitest: stat number formatting; streak grid 90 cells.
- vitest: "A line for tomorrow" autosaves with debounce.
- Playwright: end-of-day flow smoke.

## Adversarial review
- [ ] Cadence chart respects `prefers-reduced-motion`.
- [ ] "Seal day" idempotent — second click while sealed is a no-op.
- [ ] Streak intensity bucketed deterministically.
- [ ] Dossier serial format `D-YYYY-MM-DD-NNN`.
