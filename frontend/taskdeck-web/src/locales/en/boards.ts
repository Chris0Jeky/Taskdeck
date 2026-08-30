/**
 * Boards list surface (`views/BoardsListView.vue`) — English source catalog.
 *
 * `card.created` interpolates an already-formatted date string: the formatting
 * itself is done with `Intl` against the active locale in the view, not here
 * (ADR-0054 §4).
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
