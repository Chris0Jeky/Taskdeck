/**
 * Inbox surface (`views/paper/PaperInboxView.vue`) — English source catalog.
 *
 * `variant.nib` / `variant.composer` are Taskdeck's own coinages for the two
 * capture affordances (ADR-0054 §3): they stay in English in every locale, the
 * way a product feature name does.
 */
export default {
  eyebrow: 'Inbox · capture surface · {count} in queue',
  // Rendered as `{lead} <em>{emphasis}</em>` — the space before the emphasis
  // comes from the template, so `lead` must not carry a trailing space.
  title: {
    lead: "What's on your mind,",
    emphasis: 'quickly?',
  },
  lede: 'Drop the thought. It will sit here, untouched, until you triage it. Nothing flows to the board without your approval.',
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
      blocked: {
        noBoards: 'No boards yet. Create a board first, then this capture can go onto it.',
        noBoard: 'Choose a board first. Accept on board stays off until one is selected.',
        viewOnly: 'That board is view-only. Choose a board you can write to.',
      },
    },
    decision: {
      sending: 'Sending to Review…',
      rejecting: 'Rejecting…',
      nothingToPropose: 'Triage found nothing to propose — nothing was sent to Review.',
      inReview: 'Sent to Review — decide there.',
      applied: 'Applied to the board. Nothing left to do here.',
      rejected: 'Rejected. This capture will not reach Review.',
      failed: 'Triage failed, so nothing reached Review. Fix the problem, then Accept again.',
    },
    tag: {
      state: 'State: {label}. Where this capture stands right now.',
      source: 'Source: {label}. How this capture arrived — not a state.',
    },
  },
}
