/**
 * Board *detail* surface (`views/paper/PaperBoardView.vue` and its board
 * components) — English source catalog.
 *
 * Deliberately NOT `boards.ts`: that namespace is the boards *list*
 * (`views/BoardsListView.vue`). This one covers the direct-manipulation
 * controls on a single open board — add a card, edit a column, board settings.
 *
 * Only the controls added by #1945 are extracted here. The rest of
 * `PaperBoardView` is still hard-coded English; the ADR-0054 rollout is
 * surface-by-surface and finishing this surface is a separate slice.
 *
 * Wording contract (ADR-0056): every label here names a *direct* human edit
 * that takes effect immediately. Nothing in this catalog may describe a
 * proposal, an approval, or a review step — that vocabulary belongs to the
 * capture/review lane, and `card.capture` is the one door into it.
 */
export default {
  actions: {
    settings: 'Board settings',
  },
  card: {
    add: '+ card',
    addAria: 'Add a card to {column}',
    inputLabel: 'New card title',
    placeholder: 'Card title',
    submit: 'Add',
    cancel: 'Cancel',
    error: 'Could not add the card. Please try again.',
    capture: '+ capture',
    captureAria: 'Capture a note into Inbox for {column}',
  },
  column: {
    settings: 'Column settings',
    settingsAria: 'Settings for column {column}',
    moveLeft: 'Move column left',
    moveRight: 'Move column right',
  },
  columnDialog: {
    eyebrow: 'Column',
    title: 'Column settings',
    close: 'Close column settings',
    nameLabel: 'Column name',
    namePlaceholder: 'To Do',
    wipToggle: 'Set a WIP limit',
    wipLabel: 'Maximum cards',
    wipHint: 'Cards past the limit flag the column header. Leave it off for no limit.',
    save: 'Save changes',
    cancel: 'Cancel',
    delete: 'Delete column',
    deleteBlocked: 'Move or delete the cards in this column first.',
    deleteConfirm: 'Delete "{name}" and its settings? This cannot be undone.',
    deleteConfirmAction: 'Yes, delete it',
    deleteConfirmCancel: 'Keep it',
    saveError: 'Could not save the column. Please try again.',
    deleteError: 'Could not delete the column. Please try again.',
  },
  boardDialog: {
    eyebrow: 'Board',
    title: 'Board settings',
    close: 'Close board settings',
    nameLabel: 'Board name',
    namePlaceholder: 'My board',
    descriptionLabel: 'Description',
    descriptionPlaceholder: 'What is this board for?',
    save: 'Save changes',
    cancel: 'Cancel',
    lifecycle: 'Lifecycle',
    stateActive: 'Active',
    stateArchived: 'Archived',
    archiveHint:
      'Archiving hides this board from board lists. Nothing is deleted — restore it from Workspace → Archive.',
    restoreHint: 'This board is archived. Restore it to bring it back into active board lists.',
    archive: 'Move to archive',
    archiveConfirm: 'Move "{name}" to the archive? You can restore it later.',
    archiveConfirmAction: 'Yes, archive it',
    archiveConfirmCancel: 'Keep it here',
    restore: 'Restore board',
    saveError: 'Could not save the board. Please try again.',
    archiveError: 'Could not archive the board. Please try again.',
    restoreError: 'Could not restore the board. Please try again.',
  },
}
