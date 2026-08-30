/**
 * Inbox surface (`views/paper/PaperInboxView.vue`) — English source catalog.
 *
 * `variant.nib` / `variant.composer` are Taskdeck's own coinages for the two
 * capture affordances (ADR-0054 §3): they stay in English in every locale, the
 * way a product feature name does.
 *
 * `eyebrow` carries TWO counts, labelled apart (#1974). It used to read
 * "{count} in queue" over the total number of captures fetched — applied ones
 * included — while the sidebar badge counted only pending ones, so the same
 * screen showed two "queue" numbers three apart. The queue number is now the
 * pending one; the total keeps its own words and never claims to be a queue.
 * "Awaiting triage" is deliberately Home's phrase (`home.status.awaitingTriage`)
 * so the badge, Home's triage line and this eyebrow all name one thing.
 *
 * `eyebrow` is a PLURAL message chosen on `{total}`. English needs no agreement
 * — "captured" is invariable — but Italian and Spanish do ("1 catturato" /
 * "1 capturada"), and vue-i18n plural forms are whole-message alternatives, not
 * per-word ones. The catalog guard requires the same number of pipe segments in
 * every locale, so the two English forms are deliberately identical: they exist
 * to give `it`/`es` a singular slot to fill. `{pending}` needs no branch — its
 * label is invariable in all three ("awaiting triage", "da smistare",
 * "por clasificar").
 */
export default {
  eyebrow:
    'Inbox · capture surface · {pending} awaiting triage · {total} captured | Inbox · capture surface · {pending} awaiting triage · {total} captured',
  // Rendered as `{lead} <em>{emphasis}</em>` — the space before the emphasis
  // comes from the template, so `lead` must not carry a trailing space.
  title: {
    lead: "What's on your mind,",
    emphasis: 'quickly?',
  },
  lede: 'Drop the thought. It will sit here, untouched, until you triage it. Nothing flows to the board without your approval.',
  history: {
    eyebrow: 'Archive · capture history · read-only',
    title: 'Archived capture history',
    lede: 'Inspect the captures retained for this archived board. Restore the board before creating, editing, or triaging work.',
    tableTitle: 'Archived captures',
    empty: 'No retained captures were found for this archived board.',
    detail: {
      open: 'Show the full retained capture',
      close: 'Hide the full retained capture',
      title: 'Retained capture',
      loading: 'Loading the retained capture…',
      error: 'The retained capture could not be loaded.',
      captured: 'Captured',
      processed: 'Processed',
      board: 'Board',
      triageRun: 'Triage run',
      promptVersion: 'Prompt version',
      proposalLink: 'Open the decision record',
      noProposal: 'No decision record was created from this capture.',
      none: 'Not recorded',
    },
  },
  // Degraded-triage notice (#2202; backend half #2192 / #2203). A capture whose
  // LLM leg could not deliver still COMPLETES on the deterministic extractor,
  // so this is a caution on a success and never a failure — it must not borrow
  // the Failed styling or the `alert` role. `reason` interpolates the server's
  // own notice VERBATIM: it is server-authored, redacted and bounded upstream,
  // and nothing from local configuration is ever appended to it.
  //
  // PROVENANCE IS NOT ASSERTED (PR #2224 review). One of the server's own
  // notices — `CaptureTriageService.ResolveReuseDegradedNotice`, the crash
  // recovery path — deliberately reports that the reused proposal may have come
  // from EITHER the model or the deterministic extractor. So `label` and `lead`
  // may not state which engine authored the result; they say only that a model
  // reading could not be confirmed, and every mention of the deterministic
  // extractor below is conditional. Distinguishing the two would need a
  // machine-readable degradation kind from the backend (#2212), not string
  // matching on the server sentence, which the frontend never parses.
  //
  // The review guidance is STATUS-SPECIFIC (PR #2224 review): the notice rides
  // three statuses, and "read it before you apply it" is impossible on
  // `Triaged` (no proposal exists) and stale on `Converted` (already applied).
  // `triageDegradedReviewKey` in `inboxUtils.ts` picks the variant.
  degraded: {
    label: 'Triaged without a confirmed model reading',
    lead: 'Taskdeck cannot confirm that the model produced this result. The server reported the triage this way:',
    reason: 'Reported: {reason}',
    reviewProposal: 'If the deterministic offline extractor produced this proposal, it is a text-pattern guess rather than a model reading, and it carries no evidence links. Read it closely before you apply it.',
    reviewTriaged: 'Triage finished without proposing anything. That may be the deterministic offline extractor recognising no pattern rather than there being nothing to do, so read the capture yourself.',
    reviewConverted: 'This capture has already been applied to a board. Check the resulting board changes against the capture text, because the result may not have come from a model reading.',
    action: 'If the model was meant to run, check the LLM provider settings.',
  },
  capture: {
    errorLead: 'Capture not saved. Your draft is still here.',
    errorDetail: 'Details: {reason}',
    // GH-2142 -- a 401 hard-navigates to /login, so the draft is stashed in
    // sessionStorage first and restored here. `sessionExpiredReason` is the
    // synthesised receipt shown when the redirect beat the request's own error.
    sessionExpiredReason: 'Your session expired before this capture was saved.',
    draftRestoredLead: 'Draft restored.',
    draftRestoredDetail:
      'Signing in again interrupted this capture, so nothing reached Inbox. Send it when you are ready.',
    draftRestoredTruncated: 'Part of this draft was too long to keep, so some of it was not restored.',
    draftRestoredDiscard: 'Discard this draft',
    errorDiagnosticsLabel: 'Request diagnostics',
    errorFallback: 'Please try again when the connection is available.',
    metadataCompatibilityLead: 'Capture saved without its due date or labels.',
    metadataCompatibilityDetail: 'This server version ignored that metadata. Do not retry—the capture is already in Inbox.',
  },
  scope: {
    board: 'Board: {board}',
    boardAndColumn: 'Board: {board} · Column: {column}',
    clear: 'Show all captures',
  },
  empty: {
    scoped: 'No captures in {scope}. Show all captures to restore the full Inbox.',
  },
  variantToggle: {
    label: 'Capture variant',
  },
  variant: {
    nib: 'Nib',
    composer: 'Composer',
  },
  // Board pickers (the inline triage picker and the composer's board select).
  // Read-only boards stay VISIBLE but disabled and annotated (#1836): silently
  // filtering them would leave a Viewer wondering where a board went.
  boardPicker: {
    viewOnlyOption: '{name} · view-only',
    viewOnlyHint: 'Boards marked view-only need write access before anything can be triaged into them.',
  },
  // Triage row legibility (#1944). `blocked.*` says WHY the confirm button is
  // off — an unmet precondition must be visible, never silent. `decision.*` is
  // the next step after a decision, so a decided row never reads like an
  // untouched one. `tag.*` separates a capture's SOURCE from its STATE.
  triage: {
    boardPick: {
      loading: 'Loading boards…',
      loadFailed: 'Boards could not be loaded. Check your connection, then try again.',
      retry: 'Retry board load',
      blocked: {
        noBoards: 'No boards yet. Create a board first, then this capture can go onto it.',
        noBoard: 'Choose a board first. Ask AI stays off until one is selected.',
        viewOnly: 'That board is view-only. Choose a board you can write to.',
      },
    },
    decision: {
      sending: 'Sending to Review…',
      keeping: 'Keeping for later…',
      archiving: 'Archiving…',
      kept: 'Kept for later. Ask AI or archive it when you are ready.',
      archived: 'Archived. No proposal or board work was created.',
      nothingToPropose: 'Triage found nothing to propose — nothing was sent to Review.',
      inReview: 'Sent to Review — decide there.',
      applied: 'Applied to the board. Nothing left to do here.',
      rejected: 'Rejected. This capture will not reach Review.',
      failed: 'Analysis failed, so nothing reached Review. Fix the problem, then ask AI again.',
    },
    tag: {
      state: 'State: {label}. Where this capture stands right now.',
      source: 'Source: {label}. How this capture arrived — not a state.',
    },
    // Pre-triage text correction (GH-1951) — the Legacy detail panel's
    // "Edit Text" affordance, ported to the Paper row.
    //
    // `blocked.notEditable` states the FACT and not a cause. The server refuses
    // a text edit for more than one reason (a linked transcript, a status that
    // has moved on), and an API older than the flag omits it entirely; naming
    // one of those would be a guess presented as an explanation. The action
    // names below mirror the current Ask AI, Keep, and Archive controls.
    //
    // `loadFailed` / `saveFailed` interpolate the server's own words, and
    // `unknownReason` fills the slot when it gave none — so the sentence never
    // trails off into a colon with nothing after it.
    edit: {
      action: 'Edit capture',
      label: 'Capture text',
      placeholder: 'Correct the captured text…',
      hint: 'Fix the wording before Ask AI turns this into a proposal. Saving changes the capture only — nothing reaches a board from here.',
      loading: 'Loading the full capture text…',
      save: 'Save changes',
      saving: 'Saving…',
      cancel: 'Cancel',
      close: 'Close',
      retry: 'Retry',
      unknownReason: 'the server gave no reason',
      loadFailed: 'The full capture text did not load: {reason}',
      saveFailed: 'The capture changes were not saved: {reason}',
      decisionBlocked: 'Finish or cancel this edit before you Ask AI, Keep, or Archive.',
      metadata: {
        legend: 'Due date and labels',
        dueDate: 'Due date (optional)',
        labels: 'Labels (optional)',
        labelsPlaceholder: 'Type one existing label name',
        addLabel: 'Add label',
        removeLabel: 'Remove {label}',
        hint: 'Add one existing label name at a time with Enter. Remove a chip to clear it, then Save and Ask AI again to retry triage. Commas stay part of a label name; labels are never created here.',
        unavailable: 'This API did not return editable metadata. A text-only save will preserve any stored due date and labels.',
      },
      blocked: {
        notEditable: "This capture's text can't be edited. Ask AI, Keep, or Archive it as it stands.",
        empty: "Text can't be empty. Type something, or cancel to leave the capture as it was.",
        unchanged: 'Nothing has changed yet. Edit the text or metadata, or cancel to leave the capture as it was.',
        editorOpen: 'Another capture is open for editing. Save or cancel that edit first — switching now would drop the text typed there.',
        busyElsewhere: 'Another capture action is still finishing. Save comes back the moment it lands.',
      },
    },
  },
}
