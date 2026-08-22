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
 * Empty-state contract (issue 1939): `notBuilt` copy is for panels with NO
 * query behind them (ledger, decisions, boards are hardcoded empty arrays).
 * `unavailable` copy is for panels that do have a live query which failed or
 * has not resolved. Never blur the two — "not available yet" read as broken,
 * which is the defect this catalog exists to fix.
 */
export default {
  seal: {
    action: 'Seal day',
    idleStatus: 'Seal when your day is complete',
    autoStatus: 'Auto-seals in {duration}',
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
  },
  empty: {
    notBuiltTag: 'Not built yet',
    stats: 'Today’s live totals could not be loaded. Inbox and Review remain the source of truth.',
    cadence:
      'Cadence could not be loaded. It is live data rather than a missing feature — no work pattern is being inferred.',
    ledgerSummary: 'Not recorded yet',
    ledger:
      'Taskdeck does not record a per-day event ledger yet, so this panel has nothing behind it and no events are being invented. Inbox and Review show what actually happened today.',
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
