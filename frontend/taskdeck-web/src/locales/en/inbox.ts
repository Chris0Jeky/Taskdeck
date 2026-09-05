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
  // Shown INSTEAD of `eyebrow` during a scope replacement (#2501). The rows
  // those counts would be computed from belong to the scope the user just left,
  // so the eyebrow carries no count at all rather than a count about somewhere
  // else. Deliberately not a plural message: it has nothing to agree with,
  // which is the whole point.
  //
  // It also makes NO claim about the load. `isScopeReplacement` is sticky
  // across failure by design — the orchestrator swallows the throw so the
  // retained rows stay hidden — so a "loading…" word here would sit above the
  // table's own error and Retry, permanently, describing a load that had
  // already stopped. The table owns loading, error and retry; this line owns
  // only the refusal to publish a count it cannot stand behind.
  eyebrowUncounted: 'Inbox · capture surface',
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
    // GH-2141 -- the Paper composer can file a transcript without leaving the
    // skin. `transcriptNote` must stay explicit: a transcript source is what
    // routes the text to the configured assistant, and that is not something a
    // capture surface may imply.
    source: {
      legend: 'Source',
      typed: 'Typed note',
      transcript: 'Transcript',
      transcriptNote:
        'Transcript captures are sent to the configured assistant for task extraction. Typed notes are not.',
      tooLong: 'This transcript is too long. Maximum length is {max} characters.',
    },
  },
  // The Composer's own field chrome (#1871, the residual half of the #1870
  // extraction). Its own namespace and not `capture.*`: that one is the shared
  // capture vocabulary — receipts, draft restoration, the source radios the Nib
  // shares — while these four labels exist only on the Composer's form.
  //
  // The four `*Aria` names keep their pre-extraction English BYTE FOR BYTE.
  // `views/paper/PaperInboxView.spec.ts` and three Playwright specs select
  // these controls by their accessible name, so editing the English here is a
  // test change and not a copy change. That is also why `labelsAria` and
  // `dueAria` do not yet repeat their visible eyebrow first the way
  // `boardPicker.triageAria` does (WCAG 2.5.3, the PR #2675 pattern): closing
  // that gap needs those selectors migrated to testids first.
  //
  // The placeholders stay placeholders — a hint about the shape of the value,
  // never the name of the field, which is what the eyebrow and the accessible
  // name are for.
  composer: {
    bodyLabel: 'Body',
    bodyAria: 'Capture body',
    bodyPlaceholder: 'The thought, in plain language…',
    labelsLabel: 'Labels',
    labelsAria: 'Add label',
    labelsPlaceholder: 'add and press Enter',
    dueLabel: 'Due (optional)',
    dueAria: 'Due date',
    // A statement about the product, not about this draft: attachments are not
    // stored with a capture at all yet, so it never varies by row or state.
    attachmentsUnavailable: 'Attachments are not saved with captures yet.',
  },
  nib: {
    eyebrow: 'Quick capture · {shortcut}',
    destinationWithBoard: 'This capture lands in Inbox, linked to {board}, for triage.',
    destinationWithoutBoard: 'This capture lands in Inbox without a board, for triage.',
    selectedBoard: 'the selected board',
    submit: 'Capture',
  },
  // `scope.board` is the applied-filter chip and the `{scope}` inside
  // `empty.scoped`, so it may name only what the list request narrowed by. A
  // `boardAndColumn` form was removed with #1984 finding 2: the Inbox list is
  // fetched with a boardId and no column key, so naming a column here told the
  // reader a filter had been applied that never was.
  scope: {
    board: 'Board: {board}',
    clear: 'Show all captures',
  },
  empty: {
    scoped: 'No captures in {scope}. Show all captures to restore the full Inbox.',
  },
  // Appended to the capture-count line while a SAME-scope list load runs over
  // rows that stay visible and usable (#2501). Lowercase because it follows a
  // "·" separator inside that line.
  refreshing: 'refreshing…',
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
    // The eyebrow above BOTH board selects — the Composer's and the triage
    // row's — so the two pickers cannot drift apart in one locale (#1871).
    label: 'Board',
    // One visible label, two accessible names, because the two selects do
    // different things: the Composer's chooses where a NEW capture will land,
    // the triage one chooses a board for the capture already in the row.
    //
    // `composerAria` keeps its pre-extraction English exactly — see the note on
    // `composer` above; it happens to already lead with the visible label.
    // `triageAria` is free of external selectors, so it takes the full PR #2675
    // shape: the visible label first, then what the control does (WCAG 2.5.3).
    // The it/es forms mirror the English rather than expanding on it; Romance
    // word order puts the head noun first in `composerAria`, which still leaves
    // the visible label inside the accessible name.
    composerAria: 'Board picker',
    triageAria: 'Board: choose where this capture goes',
    noBoardOption: 'No board · land in inbox',
    selectPlaceholder: 'Select a board…',
    viewOnlyOption: '{name} · view-only',
    viewOnlyHint: 'Boards marked view-only need write access before anything can be triaged into them.',
  },
  // Triage row legibility (#1944). `blocked.*` says WHY the confirm button is
  // off — an unmet precondition must be visible, never silent. `decision.*` is
  // the next step after a decision, so a decided row never reads like an
  // untouched one. `tag.*` separates a capture's SOURCE from its STATE.
  triage: {
    // The capture list's region name (#1871). The heading beside it says WHICH
    // captures are listed ("Today's captures", or the archive title in
    // read-only mode); this names the region itself, so a landmark list reads
    // one stable thing in both modes.
    tableAria: 'Captured items',
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
    // Where an unsaved correction stands once its capture left the list (#1999
    // item 3) — a board-filter change, a refresh that no longer returns the
    // row, or the switch into archived history. `{capture}` is the row's own
    // excerpt, so the sentence names the same thing the list did.
    //
    // `kept` and `discarded` are receipts about a moment. `held`, `blocked` and
    // `heldUneditable` are standing statements, true for as long as they are on
    // screen, so each ends by saying what the reader can do about it.
    //
    // `kept` says "this Inbox list" on purpose. The correction lives in the
    // table for as long as the table does; promising it back after a reload
    // would be a promise this mechanism cannot keep.
    //
    // `discarded` is the only sentence about a loss, and it is reached only
    // from a status the SERVER itself would refuse the edit in. It states that
    // status rather than leaving the drop unexplained.
    draft: {
      kept: 'The unsaved correction to “{capture}” was not lost. It is held while you stay on this Inbox list, and comes back with that capture when it returns. Nothing was saved.',
      held: 'The unsaved correction to “{capture}” is still held. Choose Edit capture on that row to bring it back.',
      blocked: 'The unsaved correction to “{capture}” is still held. Another capture is open for editing — finish that one, then choose Edit capture on this row to bring the correction back.',
      heldUneditable: 'The unsaved correction to “{capture}” is still held. This list does not edit a capture that is {status}, so the correction waits here until that capture can be edited again.',
      restored: 'The unsaved correction to “{capture}” is back in the editor, over the capture as it stands now. Save it or cancel as usual.',
      discarded: 'The unsaved correction to “{capture}” was dropped: the capture is now {status}, and its text can no longer be edited. Nothing was saved.',
      dismiss: 'Dismiss these notes',
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
