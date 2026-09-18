import type { BoardFetchOptions } from '../store/board/boardCrudStore'

export const BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE =
  'Taskdeck could not refresh the card state. Reopen the board and try again.'

export interface BoardLifecycleRefreshStore {
  fetchBoard(boardId: string, options?: BoardFetchOptions): Promise<boolean | void>
}

export interface BoardLifecycleRefreshOptions {
  /** Keep live comment/editor state while reconciling the same board in place. */
  preserveCardComments?: boolean
}

/**
 * Reconcile board detail after a card archive/restore has already committed.
 *
 * Production background reads resolve `false` for refresh failures after
 * surfacing the configured warning (or the authoritative 403 boundary). The
 * catch remains a defensive adapter boundary: a mock, plugin, or future store
 * implementation must not turn this detached post-commit refresh into an
 * unhandled rejection.
 */
export async function refreshBoardAfterLifecycleChange(
  store: BoardLifecycleRefreshStore,
  boardId: string,
  options: BoardLifecycleRefreshOptions = {},
): Promise<boolean> {
  const fetchOptions: BoardFetchOptions = {
    intent: 'background',
    backgroundFailureMessage: BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE,
  }
  if (options.preserveCardComments) {
    fetchOptions.preserveCardComments = true
  }

  try {
    return await store.fetchBoard(boardId, fetchOptions) === true
  } catch {
    return false
  }
}
