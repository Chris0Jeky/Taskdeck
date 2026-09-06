/**
 * Boards list surface (`views/BoardsListView.vue`) — English source catalog.
 *
 * `card.created` interpolates an already-formatted date string: the formatting
 * itself is done with `Intl` against the active locale in the view, not here
 * (ADR-0054 §4).
 *
 * `error.*` is wider than the view. `error.retry` labels the control beside the
 * alert in `BoardsListView`, but `error.timeout` and `error.cancelled` are the
 * board STORE's boundary copy (`store/board/boardStoreHelpers.ts`, `#2689`
 * item 3), so they can surface on any screen the board store backs. They live
 * in this namespace because it is the board store's own catalog; do not treat
 * them as boards-list-only strings when translating.
 */
export default {
  eyebrow: 'Workspace',
  title: 'My Boards',
  newBoard: '+ New Board',
  create: {
    title: 'Create New Board',
    nameLabel: 'Board name',
    namePlaceholder: 'Board name',
    submit: 'Create',
    cancel: 'Cancel',
  },
  loading: 'Loading boards...',
  error: {
    retry: 'Retry board load',
    timeout:
      'The request took too long, so it was stopped. Check your connection, then try again.',
    cancelled: 'The request was stopped before it finished. Try again.',
  },
  empty: {
    title: 'No boards',
    hint: 'Get started by creating a new board.',
    cta: '+ Create Board',
  },
  card: {
    openLabel: 'Open board: {name}',
    noDescription: 'No description',
    created: 'Created {date}',
  },
}
