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
}
