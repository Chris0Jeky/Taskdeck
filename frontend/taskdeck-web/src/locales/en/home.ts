/**
 * Home surface (`views/paper/PaperHomeView.vue`) — English source catalog.
 *
 * Day-boundary contract (#1768): the workload counters are pure STATUS counts,
 * with no date predicate anywhere in the chain. Nothing in this catalog — in any
 * locale — may describe them as belonging to a particular day ("from yesterday",
 * "carry-over", "overnight"). A capture saved seconds ago is `New` and would be
 * mislabelled in every timezone.
 */
export default {
  eyebrow: 'Workspace · {period}',
  period: {
    morning: 'morning',
    afternoon: 'afternoon',
    evening: 'evening',
  },
  // Not composed from a shared "Good {period}" — the greeting is a single fixed
  // expression in most languages (Buongiorno / Buenas tardes) and does not
  // survive being assembled from parts.
  greeting: {
    morning: 'Good morning',
    afternoon: 'Good afternoon',
    evening: 'Good evening',
    anonymous: 'Hello',
  },
  loading: 'Loading your workspace summary...',
  error: 'Workspace summary could not be loaded.',
  lede: {
    awaitingReview: '{count} awaiting review',
    awaitingTriage: '{count} awaiting triage',
  },
  queue: {
    label: 'Queued for you',
    title: 'II · Queued for you',
    tagProposed: 'PROPOSED',
    tagTriage: 'TRIAGE',
    triageCard: 'Triage {count} capture | Triage {count} captures',
    triageCardMore: 'Triage a capture',
    triageMeta: 'inbox · awaiting decision',
  },
  firstBoard: {
    title: 'Shape your first useful board.',
    body: 'Start blank or reuse a starter workflow. The existing setup guide will create the board and take you straight into it.',
    cta: 'Start guided setup',
  },
  empty: 'Nothing waiting. Good.',
  milestones: {
    eyebrow: 'III · Your first loop',
    title: 'From thought to trusted action',
    // Shown instead of `title` once every milestone is ticked: the block has
    // stopped being an instruction and is only a receipt (#1936).
    completeTitle: 'Your first loop is complete',
    progress: '{completed}/{total} complete',
    stepComplete: 'Complete',
    stepIncomplete: 'Not complete',
    expand: 'Show milestones',
    collapse: 'Hide milestones',
    dismiss: 'Dismiss',
    note: 'These milestones stay in this workspace; they are not sent as analytics.',
  },
  capture: {
    label: 'Quick capture',
    inputLabel: 'Capture a thought',
    placeholder: 'Capture a thought...',
    errorLead: 'Capture not saved. Your text is still here.',
    errorDetail: 'Details: {reason}',
    errorDiagnosticsLabel: 'Request diagnostics',
    errorFallback: 'Please try again when the connection is available.',
  },
}
