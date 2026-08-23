/**
 * Today surface (`views/paper/PaperTodayView.vue`) — English source catalog.
 *
 * Scope: this catalog covers the seal control, the note affordance, and the
 * panel empty states — the legibility surface extracted for issue 1939. The
 * remaining Today strings (section headings, load/error/refresh banners) are
 * still literals in the SFC; extract them in a later slice.
 *
 * Seal honesty contract (issue 1939): a seal writes `DailySnapshot.SealedAt`
 * and nothing else. The domain entity has a `Seal()` transition and no inverse,
 * and the API exposes only POST/GET `/today/seal` — so no copy here, in any
 * locale, may offer an undo, nor claim that sealing archives, locks, hides, or
 * deletes anything. Nothing else in the app reads the seal today.
 *
 * There is deliberately NO auto-seal string here (GH-1939). Nothing auto-seals
 * a day: `useTodayDossier` hardcodes `autoSealsIn: null` and never overrides
 * it, and no backend service seals on a timer. `docs/STATUS.md` still asserts
 * the opposite; that line is corrected in the wave docs sweep, not here. Do not
 * reintroduce an "Auto-seals in …" message on the strength of that doc line.
 *
 * Empty-state contract (issue 1939): `notBuilt` copy is for panels with NO
 * query behind them (ledger, decisions, boards are hardcoded empty arrays).
 * `unavailable` copy is for panels that do have a live query which failed.
 * Never blur the two — "not available yet" read as broken, which is the defect
 * this catalog exists to fix.
 *
 * `loading.*` is the third class (issue 1983) and it is not optional: a live
 * panel is unavailable-because-it-failed only once its request has settled.
 * Before that it is loading, and saying "could not be loaded" mid-flight is the
 * same false report the contract above exists to prevent.
 *
 * "Not built yet" is a claim about the PANEL, never about the database (issue
 * 1983). Board and card mutations are written to audit history and are readable
 * from Activity; what Today lacks is a per-day ledger query to assemble them
 * into a day view. Copy here may say the view does not exist. It may not say
 * the events are not recorded.
 */
export default {
  seal: {
    action: 'Seal day',
    idleStatus: 'Seal when your day is complete',
    confirmTitle: 'Seal today? This cannot be undone.',
    confirmEffect:
      'Sealing stamps today with a seal time and marks the day done here. Nothing is archived, locked, hidden, or deleted — your captures, proposals, and boards keep working exactly as before.',
    confirmIrreversible: 'Taskdeck has no unseal action, so today stays sealed once you confirm.',
    confirmAction: 'Seal the day',
    confirmCancel: 'Keep the day open',
    sealingAction: 'Sealing…',
    sealedAction: 'Day sealed',
    sealedStatus: 'Sealed for the day',
    sealedReason: 'Today is sealed. Taskdeck has no unseal action, so nothing here can reopen it.',
    toastSealed: 'Day sealed. Today is marked done, and it cannot be unsealed.',
    toastFailed: 'Failed to seal the day. Please try again.',
  },
  note: {
    action: 'Write a note',
    hint: 'Goes to your line for tomorrow, below.',
    sectionSub: 'Saved with today’s date · you see it when you reopen Today',
    meta: 'saved with today’s date',
    metaFailed: 'save not confirmed · edit again to retry',
  },
  loading: {
    cadence: 'Loading today’s cadence…',
    streak: 'Loading your streak…',
  },
  empty: {
    notBuiltTag: 'Not built yet',
    stats: 'Today’s live totals could not be loaded. Inbox and Review remain the source of truth.',
    cadence:
      'Cadence could not be loaded. It is live data rather than a missing feature — no work pattern is being inferred.',
    ledgerSummary: 'No per-day view yet',
    ledger:
      'Today is not wired to the activity log yet, so this panel cannot assemble a per-day ledger and no events are being invented. Your board and card changes are still recorded in audit history — open Activity to read it, and Review for the decisions behind it.',
    decisions:
      'Taskdeck does not record a per-day decision log yet, so this panel has nothing behind it. Open Review for live proposals and the decisions you gave them.',
    boards:
      'Taskdeck does not record which boards you touched today, so this panel has nothing behind it. Open Boards for live board state.',
    carryOverNone: 'No overdue cards in today’s live summary.',
    carryOverUnavailable: 'Carry-over could not be loaded. Open Boards for live cards.',
    streak:
      'Your streak could not be loaded. It is live data rather than a missing feature — no activity history is being inferred.',
  },
}
