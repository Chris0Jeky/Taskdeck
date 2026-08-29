/**
 * Review surface — English source catalog (ADR-0054 §8 rollout step 2, `#1770`).
 *
 * Owning SFCs, all under `src/`:
 *   `views/paper/PaperReviewView.vue` (orchestrator) and its column components
 *   `views/paper/review/Review{Main,QueueRail,QueueItem,RecentApplied,DecisionRail,
 *    ChangeSection,Provenance,SideEffects,Conflicts,History,RightRail,AuthorCard,
 *    WhyNow,SimilarPast,KeysCard,MiniCadence,RevisionEditor}.vue`,
 *   the shared dialogs `components/review/{ApplyToBoardDialog,ProvenanceDrawer,
 *    TranscriptEvidenceViewer}.vue`,
 *   and the composables `composables/{useReviewActions,useReviewProposals,
 *    usePaperReviewSelectors}.ts`.
 *
 * ── Semantic contracts a translator MUST NOT break ──────────────────────────
 *
 * 1. HOW THE COMPOSABLES RESOLVE THESE KEYS. `useI18n()` needs a component
 *    instance, and `useReviewActions` / `useReviewProposals` /
 *    `usePaperReviewSelectors` are plain composables also called from module
 *    scope-adjacent code and from specs that never mount a component. They
 *    therefore import the module-scoped runtime (`src/i18n`) and call
 *    `i18n.global.t(...)` rather than taking a `t` parameter from every caller.
 *    Chosen because (a) it keeps every existing call site's signature — no
 *    surface has to thread `t` through, and the Legacy shell shares two of these
 *    composables; (b) `i18n.global.t` reads `i18n.global.locale` internally, so
 *    a call inside a `computed` (the summary cards, the side-effects fallback)
 *    still re-evaluates on a language switch. The cost is that the composables
 *    are bound to the one app-wide i18n instance, which is exactly what
 *    `main.ts` installs.
 *
 * 2. BACKEND WIRE VALUES ARE NEVER KEYS. `PendingReview`, `Approved`, `Applied`,
 *    `Rejected`, `Failed`, `Expired`, `Dismissed`, `Transcript`, `Chat`,
 *    `Queue`, `High`, `Critical` are compared as literals in code. Only their
 *    RENDERED labels live here (`status.*`, `statusInline.*`, `author.actor.*`).
 *
 * 3. API-SOURCED TEXT IS NOT TRANSLATED. Proposal titles and summaries, board
 *    names, provenance row keys/values/icons, evidence reasons, side-effect
 *    rows, conflict keys/values, history events, similar-past titles and dates,
 *    confidence component keys, backend validation messages, and revision payload
 *    field names all arrive from the server and are interpolated as `{placeholders}`.
 *    A server-supplied `component.key` renders verbatim and is UNTRANSLATABLE from
 *    here — that is a backend-localisation concern.
 *
 * 4. `age.*` are the queue rail's compact age suffixes. They are appended to a
 *    bare number, so they must stay ONE OR TWO CHARACTERS or the rail wraps.
 *
 * 5. Dates and times go through `Intl` against the ACTIVE locale (ADR-0054 §4),
 *    never through a pattern in this file.
 */
export default {
  // ── Left column: queue rail ───────────────────────────────────────────────
  queueRail: {
    eyebrow: 'Queue · {awaiting} awaiting · {stale} stale',
    eyebrowScoped: 'Queue · {awaiting} awaiting in this board · {stale} stale',
    liveAnnounce: '{count} proposal awaiting review. | {count} proposals awaiting review.',
    filters: {
      label: 'Queue filters',
    },
    filter: {
      all: 'All',
      mine: 'Mine',
      stale: 'Stale',
    },
    riskNote:
      'Risk order: Low, Medium, High, Critical. Sorting only changes presentation; review actions remain manual.',
    fileAway: {
      cta: 'File away {count} settled',
      label: 'File away {count} settled proposals',
    },
    empty: 'Nothing in this filter.',
    cadence: {
      heading: 'This week',
      // Rendered as `{label} <b>{percentage}</b>` — the value carries its own
      // emphasis in the template, so this label must not end with punctuation.
      applyRateLabel: 'Apply rate',
      applyRateEmpty: 'No decisions yet',
    },
  },

  batchApprove: {
    selectLabel: 'Select {title} for batch approval',
    request: 'Review {count} selected approval | Review {count} selected approvals',
    requestLabel: 'Open confirmation for {count} selected proposal | Open confirmation for {count} selected proposals',
    limitReached: 'A batch can contain at most {count} proposals.',
    selectionChanged: 'The selection changed because one or more proposals are no longer eligible. Review the selection again.',
    receiptMismatch: 'Taskdeck could not confirm the complete batch. Review the queue before trying again.',
    approved: 'Approved {count} proposal — not applied. | Approved {count} proposals — not applied.',
    failed: 'The selected proposals could not be approved.',
    dialog: {
      title: 'Approve selected proposals?',
      description: 'Confirm approval for {count} proposal | Confirm approval for {count} proposals',
      body: 'Taskdeck will re-check all {count} proposal and approve the whole set or none of it. | Taskdeck will re-check all {count} proposals and approve the whole set or none of it.',
      notApplied: 'This records approval only. Nothing is applied to a board.',
      cancel: 'Keep reviewing',
      confirm: 'Approve {count} proposal | Approve {count} proposals',
    },
  },

  scope: {
    board: 'Board: {board}',
    clear: 'Show all boards',
  },

  historyMode: {
    notice: 'Archived decision history · read-only. Restore the board before approving, rejecting, applying, editing, deferring, or filing proposals.',
  },

  queueItem: {
    noSummary: '(no summary)',
    confidence: 'conf {value}',
    reach: '{count} op | {count} ops',
    who: {
      assistant: 'assistant',
      capture: 'capture',
    },
  },

  // Compact age suffixes — see contract 4 above.
  age: {
    seconds: '{value}s',
    minutes: '{value}m',
    hours: '{value}h',
    days: '{value}d',
  },

  // Aria-label for the queue rail's week sparkline; the bar count is real, never assumed.
  cadence: {
    ariaLabel: 'Activity for the last {count} day | Activity for the last {count} days',
  },

  recent: {
    heading: 'Recently applied',
    empty: 'Nothing applied yet today.',
    noSummary: '(applied)',
    age: '{age} ago',
    openLabel: 'Open applied proposal: {title}',
  },

  appliedRecord: {
    ariaLabel: 'Applied proposal decision record',
    tagstamp: 'APPLIED \u00b7 READ-ONLY',
    eyebrow: 'Historical record',
    heading: 'Applied decision record',
    lede:
      'This proposal has already changed the board. Its recorded decision and effective operations are read-only.',
    filingSummary: 'Historical record \u00b7 filing only',
    historicalNotice: 'Historical applied record. No further review action is available.',
    field: {
      outcome: 'Outcome',
      decision: 'Decision',
      decisionActor: 'Decision actor',
      decisionTime: 'Decision time',
      appliedTime: 'Applied time',
    },
    value: {
      applied: 'Applied',
      approved: 'Approved',
      notRecorded: 'Not recorded',
    },
    operations: {
      heading: 'Operations applied',
    },
  },

  // ── Centre column: header, decision rail, sections ────────────────────────
  main: {
    tagstamp: 'PROPOSED · DIFF',
    ledeFallback:
      'Awaiting decision. Review the change, provenance, and side-effects below before applying.',
    dial: {
      modelCaption: 'MODEL',
      derivedCaption: 'DERIVED',
      modelReported: 'Reported item average',
      derived: 'Verification average',
      deterministic: 'DETERMINISTIC',
      notReported: 'NOT REPORTED',
      noModelNumber: 'No model confidence number',
    },
    approvedBanner: {
      title: 'Approved — not yet applied to the board.',
      // `{action}` is the primary button's own label, interpolated so the banner
      // can never name a button that says something else.
      //
      // #1942: this used to end "you will be asked to confirm" — the banner
      // announcing the app's own redundant third step. Approve now hands
      // straight to the apply confirmation, so there is exactly one step left
      // and the copy names it instead of warning about another one.
      body: 'One step left: press ⏎ (or “{action}”) to write it to the board. Nothing changes until you do.',
    },
    decisionReceipt: {
      approved: {
        title: 'Approved — not yet applied to the board.',
        body: 'Review stays here. Choose {action} when you are ready to make the board change.',
      },
      applied: {
        title: 'Applied to the board.',
        body: 'This proposal remains inspectable here; find it again under Recently Applied.',
      },
      rejected: {
        title: 'Rejected.',
        body: 'This proposal was not applied and remains inspectable here.',
      },
      deferred: {
        title: 'Deferred.',
        body: 'This proposal will return to Review when its snooze ends.',
      },
    },
    keyHint: {
      fileAway: 'PRESS ⌫ TO FILE AWAY',
      confirmApply: 'PRESS ⏎ TO APPLY TO BOARD',
      approve: 'PRESS ⏎ TO APPROVE · ⌫ TO REJECT',
    },
    footer: 'REVIEW · {serial} · LOCAL-FIRST · LEDGER',
  },

  decisionRail: {
    toolbar: {
      decision: 'Decision actions',
      filing: 'Filing actions',
    },
    stamp: {
      decision: 'DECISION',
      settled: 'SETTLED',
    },
    summary: {
      none: 'Nothing to decide right now',
      operations:
        '{count} operation · explicit review · atomic apply | {count} operations · explicit review · atomic apply',
    },
    step: {
      approve: 'Step 1 of 2 · approving does not change the board',
      execute: 'Step 2 of 2 · this writes it to the board',
    },
    reject: 'Reject',
    requestEdit: 'Request edit',
    defer: 'Defer',
    apply: {
      approve: 'Approve',
      // #1942: named for the action itself, matching the dialog's accept
      // button. "Confirm apply" described a step that only opened another
      // confirmation — that middle step is gone.
      execute: 'Apply to board',
      approveLabel: 'Approve proposal — step 1 of 2, does not change the board yet',
      executeLabel: 'Apply to board — step 2 of 2, writes this change to the board',
    },
    fileAway: {
      label: 'File away',
      ariaLabel: 'File away proposal',
    },
    // GH-1964: the rail names the lock and carries its exit. The note is the
    // accessible description of the four disabled buttons, so it must say what
    // is holding them AND what ends it — not just "busy".
    editLock: {
      editing: 'Editing this proposal below — decisions resume when you save or cancel the edit.',
      saving: 'Saving your edit — decisions resume when it lands.',
      cancel: 'Cancel edit',
    },
  },

  // § I — the change
  change: {
    title: 'The change',
    subTitle: '{count} operation · {board} | {count} operations · {board}',
    beforeEyebrow: 'Before · today',
    beforeEyebrowApplied: 'Before · recorded',
    afterEyebrow: 'After · on apply',
    afterEyebrowApplied: 'After · applied',
    fieldsHeading: 'Per-field changes',
    tag: {
      new: '· new',
      kept: '· kept',
    },
    before: {
      titleFallback: 'No proposal selected',
      bodyFallback: 'Review {count} proposal operations before applying.',
      // Primary copy for a settled Applied record, NOT a fallback: it deliberately wins over
      // the backend's prospective `presentation.impactSummary` (#2117).
      bodyApplied: 'Recorded {count} proposal operation. | Recorded {count} proposal operations.',
      meta: '{board} · {source}',
      sourceFallback: 'proposal',
    },
    after: {
      noParameterPreview: 'No parameter preview supplied for this operation.',
      noPreviewTitle: 'No operation preview',
      noPreviewBody: 'The proposal did not include operation details.',
    },
    fields: {
      operationsKey: 'operations',
      none: 'none',
      notProvided: 'not provided',
    },
  },

  // § II — provenance
  provenance: {
    title: 'Provenance',
    sub: "What was read · what wasn't · what was inferred",
    empty: 'Provenance not available for this proposal yet.',
    details: {
      show: 'Show provenance details',
      hide: 'Hide provenance details',
    },
    // One sentence per recorded engine, selected by `views/paper/review/provenanceActor.ts`
    // from the proposal's own provenance. There is deliberately NO unconditional variant:
    // the surface says nothing when the record is absent or incoherent (GH-1963). `{label}`
    // is the backend `provider/model` identifier — wire text, interpolated verbatim.
    footnote: {
      deterministic:
        'Recorded provenance: {label} — Taskdeck’s deterministic offline extractor produced this proposal.',
      mock: 'Recorded provenance: {label} — Taskdeck’s built-in mock provider produced this proposal, not a live model.',
      provider:
        'Recorded provenance: {label} — your configured AI provider produced this proposal, so its source text was sent to that provider.',
    },
    viewAll: 'View full read-set →',
  },

  provenanceDrawer: {
    ariaLabel: 'Provenance details',
    title: 'Provenance',
    close: 'Close provenance drawer',
    meta: {
      model: 'Model',
      confidence: 'Confidence',
      confidenceValue: '{value}%',
      latency: 'Latency',
      latencyValue: '{value}ms',
      promptVersion: 'Prompt version',
    },
    weight: {
      primary: 'Primary Sources',
      contextual: 'Contextual',
      inferred: 'Inferred',
      excluded: 'Excluded',
    },
    evidenceTitle: 'Evidence Links',
    evidenceSpan: 'chars {start}–{end}',
    viewTranscript: 'View in transcript',
    hideTranscript: 'Hide transcript',
    copyJson: 'Copy JSON',
    copied: 'Copied!',
    copyFailed: 'Copy failed',
    report: 'Report bad suggestion',
  },

  transcript: {
    title: 'In transcript',
    close: 'Close',
    speaker: 'Speaker: {name}',
    loading: 'Loading transcript…',
    unresolved: 'This evidence span no longer resolves against the stored transcript.',
    error: {
      notFound: 'This transcript is no longer available.',
      unauthorized: 'You are not signed in to view this transcript.',
      generic: 'The transcript could not be loaded. Try again.',
    },
  },

  // § III — side effects
  sideEffects: {
    title: 'Side effects',
    sub: "What lands · what doesn't · what archives",
    empty: 'No declared side-effects.',
    riskEyebrow: 'Apply considerations',
    // Shown when the deep-review side-effects request failed or has not landed;
    // the real summary/description are server-supplied.
    fallback: {
      summary: 'Risk details unavailable',
      description: 'Review the declared side effects before applying.',
    },
  },

  // § IV — conflicts & warnings
  conflicts: {
    title: 'Conflicts & warnings',
    sub: {
      clear: 'What the system noticed · clear',
      counted:
        'What the system noticed · {count} minor | What the system noticed · {count} items',
    },
    empty: 'Nothing flagged.',
    tone: {
      warn: 'WARNING',
      ok: 'CLEAR',
      info: 'INFO',
    },
  },

  // § V — history
  history: {
    title: 'History · this card',
    sub: 'Every touch since creation',
    empty: 'No history recorded.',
    status: {
      pending: 'PENDING',
      applied: 'APPLIED',
      past: 'past',
      unknown: 'UNKNOWN',
    },
  },

  // ── Right column ──────────────────────────────────────────────────────────
  author: {
    heading: 'Author',
    confidenceHeading: 'Confidence source',
    modelReportedHeading: 'Model-reported item confidence',
    details: {
      show: 'Show confidence details',
      hide: 'Hide confidence details',
    },
    nameFallback: 'Proposal',
    // `{source}` is the lowercased backend source type (`chat`, `queue`, …).
    name: '{actor} · {source} proposal',
    modelConfidence: '{value} model-reported average',
    derivedConfidence: '{value} derived average',
    deterministic: 'Deterministic extraction · no model confidence',
    notReported: 'No model confidence reported',
    actor: {
      assistant: 'Assistant',
      capture: 'Capture',
    },
  },

  whyNow: {
    heading: 'Why now',
    noProposal: 'No proposal is selected.',
    fallback: 'This proposal is awaiting review based on the source captured with it.',
  },

  similarPast: {
    heading: 'Similar past decisions',
    empty: 'No comparable past decisions.',
    details: {
      show: 'Show similar decisions',
      hide: 'Hide similar decisions',
    },
    verdict: {
      applied: 'APPLIED',
      rejected: 'REJECTED',
    },
    rateLabel: 'Apply rate on similar:',
    rateValue: '{applied} of {total} ({percent}%)',
  },

  keys: {
    heading: 'Decide with keys',
    // Only `space` is a word; ⏎ ⌫ E D P are physical keys and stay as they are.
    spaceKey: 'space',
    enter: {
      approve: 'Approve proposal · step 1 of 2',
      execute: 'Apply to board · step 2 of 2',
    },
    edit: 'Request edit · opens composer',
    reject: 'Reject · with optional reason',
    defer: 'Defer 1h',
    provenance: 'Toggle provenance pane',
    preview: 'Preview diff in card detail',
  },

  // ── Revision editor ───────────────────────────────────────────────────────
  revisionEditor: {
    stamp: 'EDIT BEFORE APPROVE',
    // Announced when focus moves into the composer on entry (GH-1964).
    regionLabel: 'Edit this proposal before approving it',
    jsonError: 'Enter valid JSON before saving.',
    reasonLabel: 'Reason for edit',
    reasonPlaceholder: 'Why are you editing this proposal?',
    cancel: 'Cancel',
    save: 'Save revision',
    badge: '{count} revision | {count} revisions',
  },

  // ── Technical details disclosure ──────────────────────────────────────────
  technical: {
    summary: 'Technical details',
    copy: 'Copy technical details',
    copied: 'Copied',
    ariaLabel: 'Proposal technical details',
  },

  // ── Inline diff pane ──────────────────────────────────────────────────────
  diff: {
    serial: '§ DIFF',
    title: 'Operation details',
    hint: 'Press Space to hide',
    loading: 'Loading diff…',
    storedBanner:
      '{status} · read-only — showing the stored preview from the original submission.',
    // Rendered `✎ {lead} <strong>{emphasis}</strong> {tail}` — the spaces come
    // from the template, so `lead` must not carry a trailing space.
    revised: {
      lead: 'This proposal was',
      emphasis: 'revised',
      storedTail:
        'after submission — the stored preview shows the original operations, not the revised ones.',
      fallbackTail:
        'after submission — the recorded operations show the original submission, not the revised one.',
    },
    liveCaveat: {
      lead: 'This preview reflects your latest',
      emphasis: 'saved edit',
      tail: '— the revised operations, not the original proposal.',
    },
    invalid: {
      // `{reason}` is the backend's OWN validation message when it supplied one
      // (it is not translated); `noOperations` is the local fallback wording.
      line: '{reason} — Apply will reject this proposal.',
      noOperations: 'This proposal contains no operations to apply',
    },
    storedEmpty: 'No stored preview is available for this proposal.',
    empty: 'No changes to preview for this proposal.',
    storedAriaLabel: 'Stored proposal preview',
    liveAriaLabel: 'Proposal operation diff',
    recordedAriaLabel: 'Recorded proposal operations',
  },

  // ── Phase-2 confirmation dialog (shared with the Legacy shell) ────────────
  applyDialog: {
    title: 'Apply to the board?',
    // #1942: this dialog now opens straight after approve, so it is the ONE
    // remaining step rather than a confirmation of a confirmation. The copy
    // states what already happened (approved) and what this click does.
    lede: 'Approved. Nothing has been written to the board yet — this is the step that applies it.',
    noSummary: 'This proposal has no summary.',
    revisionNote:
      'This proposal was edited — its latest saved revision is what will be applied, not the original operations.',
    contentsWillApply: 'The approved contents of this proposal will be applied.',
    operationsWillApply: '{count} operation will be applied. | {count} operations will be applied.',
    // "Not yet" rather than "Cancel": dismissing does not undo the approval, it
    // just leaves the proposal approved-but-not-applied (#1942).
    cancel: 'Not yet',
    confirm: 'Apply to board',
  },

  // GH-1969 — the in-app reason collector that replaced `window.prompt`. The
  // required and optional labels are separate keys, never one string plus a
  // conditional suffix: they are different sentences in Italian and Spanish.
  rejectDialog: {
    title: 'Reject this proposal?',
    lede: 'Rejecting closes this proposal. Nothing on the board changes.',
    noSummary: 'This proposal has no summary.',
    reasonOptionalLabel: 'Reason (optional)',
    reasonRequiredLabel: 'Reason (required)',
    reasonPlaceholder: 'Why is this not going ahead?',
    // The reason is read later by whoever asks why this did not ship, which is
    // the whole argument for collecting it somewhere the product can style,
    // translate and test.
    requiredNote: 'High and critical risk proposals need a recorded reason.',
    cancel: 'Keep it',
    confirm: 'Reject proposal',
  },

  // ── Empty surface ─────────────────────────────────────────────────────────
  empty: {
    eyebrow: 'Queue · {count} awaiting',
    title: 'Nothing waiting. Good.',
    body: 'When the assistant has something to propose it will appear here for review.',
    loading: 'Loading proposals…',
    accessRevoked: {
      title: 'This review queue is no longer available to you.',
      body: 'Your access to these boards changed, so the queue was cleared and has stopped updating. Reload or pick a board you can still reach.',
    },
    scoped: {
      title: 'No proposals in {scope}.',
      body: 'This review list is limited to the active board. Show all boards to restore the full queue.',
    },
    filtered: {
      title: 'No matches in {filter}.',
      body: 'Switch filters to review proposals that are still waiting elsewhere in the queue.',
    },
    unavailable: {
      eyebrow: 'Requested proposal',
      title: 'This proposal is unavailable.',
      body: 'Proposal {id} is no longer available to review. It may have been applied, archived, or removed.',
      return: 'Back to Review',
    },
  },

  // ── Legacy-shell summary cards (data built in `useReviewProposals`) ───────
  summary: {
    pendingReview: {
      label: 'Pending review',
      helper: 'Changes waiting for an explicit decision.',
    },
    readyToExecute: {
      label: 'Ready to execute',
      helper: 'Approved proposals that can now land on boards.',
    },
    captureLinked: {
      label: 'Capture-linked',
      helper: 'Review items that came through the inbox loop.',
    },
    applied: {
      label: 'Applied',
      helper: 'Proposals already executed successfully.',
    },
  },

  // Rendered status labels. The wire values themselves are never keys.
  status: {
    pendingReview: 'Pending review',
    approved: 'Approved',
    applied: 'Applied',
    rejected: 'Rejected',
    failed: 'Failed',
    expired: 'Expired',
    dismissed: 'Dismissed',
  },

  // The same statuses in the header's running-text voice.
  statusInline: {
    pendingReview: 'awaiting decision',
    approved: 'approved',
    applied: 'applied',
    rejected: 'rejected',
    failed: 'failed',
    expired: 'expired',
    dismissed: 'dismissed',
  },

  // `{time}` is Intl-formatted against the active locale, never a pattern here.
  headerMeta: '{time} · {status}',

  // ── Toasts ────────────────────────────────────────────────────────────────
  // The `prompt.*` pair that used to live here fed the native `window.prompt`
  // for the rejection reason; GH-1969 moved that copy into `rejectDialog.*`.
  toast: {
    approved: 'Proposal approved for board application',
    approveFailed: 'Failed to approve proposal',
    rejected: 'Proposal rejected',
    rejectFailed: 'Failed to reject proposal',
    rejectReasonRequired: 'Rejection reason is required for high and critical risk proposals',
    snoozed: 'Snoozed for 1 hour — it will return to your queue.',
    snoozeFailed: 'Failed to snooze proposal',
    applied: 'Proposal applied to board',
    applyFailed: 'Failed to apply proposal to board',
    dismissed: 'Proposal dismissed',
    dismissedRefreshing: 'Proposal removed from view. Refreshing...',
    dismissFailed: 'Failed to dismiss proposal',
    nothingToClear: 'No completed proposals to clear.',
    cleared: 'Cleared {count} completed proposal. | Cleared {count} completed proposals.',
    clearFailed: 'Failed to clear proposals',
    diffFailed: 'Failed to load proposal diff',
    loadProposalFailed: 'Failed to load proposal',
    loadProposalsFailed: 'Failed to load proposals',
    noLongerAvailable: 'This proposal is no longer available to you.',
    feedbackRecorded: 'Feedback recorded for this suggestion.',
    feedbackFailed: 'Failed to record feedback',
    noProposalToReport: 'No proposal is selected to report.',
    provenanceToggleUnwired: 'Provenance toggle is not wired yet; provenance is rendered inline below.',
    revisionBusyFileAway: 'Save or cancel the revision before filing this proposal away.',
    revisionBusyApply: 'Save or cancel the revision before applying this proposal.',
    revisionBusyReject: 'Save or cancel the revision before rejecting this proposal.',
    revisionBusyDefer: 'Save or cancel the revision before deferring this proposal.',
    notDismissableYet: 'This proposal is still active and cannot be filed away yet.',
    bulkBusy: 'Wait for the current action to finish before filing more away.',
    notApplyable: 'This proposal is no longer actionable. Refresh review to see current status.',
    revisionStateUnknown:
      'Revision history is unavailable, so this proposal cannot be verified for Apply. Try again.',
    zeroOpApproved: 'This proposal contains no operations — applying it to the board will be rejected.',
    zeroOpPending:
      'This proposal contains no operations to apply — Apply will reject it. Reject or file it away instead.',
    notRejectable: 'This proposal can no longer be rejected. Refresh review to see current status.',
    notEditable: 'This proposal can no longer be edited.',
    notDeferrable: 'This proposal can no longer be deferred.',
  },
}
