/**
 * Board CRUD operations: fetch, create, update, delete boards.
 */
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { labelsApi } from '../../api/labelsApi'
import axios from 'axios'
import { BOARD_REQUEST_TIMEOUT_MS, type BoardReadOptions } from '../../api/http'
import { buildDemoBoardList, buildDemoBoardDetail } from '../../utils/demoData'
import type { CreateBoardDto, UpdateBoardDto } from '../../types/board'
import type { BoardState, CardFilters } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

// Minimum gap between board-list fetches.  Multiple views (BoardsListView,
// ActivityView, ReviewView, etc.) can call fetchBoards on mount in quick
// succession; the throttle guard prevents duplicate network round-trips.
const FETCH_BOARDS_THROTTLE_MS = 5000
const BOARD_ACCESS_REVOKED_MESSAGE = 'You no longer have access to this board'

export type BoardFetchIntent = 'explicit' | 'background'

export interface BoardFetchOptions {
  intent?: BoardFetchIntent
}

interface ActiveBoardFetch {
  boardId: string
  intent: BoardFetchIntent
  generation: number
  controller: AbortController
  promise: Promise<boolean>
}

interface QueuedBackgroundBoardFetch {
  boardId: string
  promise: Promise<boolean>
  resolve: (committed: boolean) => void
}

export function createBoardCrudActions(state: BoardState, helpers: BoardHelpers) {
  let lastFetchBoardsAt = 0
  let unfilteredFetchBoardsInFlight: Promise<void> | null = null
  let boardListGeneration = 0
  let boardFetchGeneration = 0
  let activeBoardFetch: ActiveBoardFetch | null = null
  let queuedBackgroundBoardFetch: QueuedBackgroundBoardFetch | null = null

  async function fetchBoards(search?: string, includeArchived = false) {
    const now = Date.now()
    // Allow forced refreshes (search/archive filter changes) to bypass throttle.
    const isFilteredRequest = !!search || includeArchived
    // The throttle stamp is only written after a success, so it cannot separate
    // two callers that mount together on an empty store — the inbox composer
    // and the triage table do exactly that.  Before this share they issued
    // parallel unfiltered reads, and one could fail while the other confirmed
    // an empty account, leaving the picker's three states disagreeing (#1961).
    // Filtered reads are excluded in both directions: they never join this
    // share and never populate it, because their payload is a different list.
    if (!isFilteredRequest && unfilteredFetchBoardsInFlight) {
      return unfilteredFetchBoardsInFlight
    }
    if (!isFilteredRequest && now - lastFetchBoardsAt < FETCH_BOARDS_THROTTLE_MS) {
      return
    }
    if (helpers.isDemoMode) {
      state.loading.value = true
      state.error.value = null
      state.boards.value = buildDemoBoardList()
      state.loading.value = false
      return
    }

    const requestGeneration = boardListGeneration
    const isCurrentListGeneration = () => requestGeneration === boardListGeneration

    const request = (async () => {
      try {
        state.loading.value = true
        state.error.value = null
        const freshBoards = await boardsApi.getBoards(search, includeArchived)
        // A logout reset ran while this read was on the wire.  The payload
        // belongs to the account that is now signed out, so neither it nor the
        // throttle stamp it would write may reach the next session's store —
        // and dropping the stamp is what lets that session refetch at once.
        if (!isCurrentListGeneration()) {
          return
        }
        lastFetchBoardsAt = Date.now()
        state.boards.value = freshBoards

        // Preserve selection guard: only update activeBoardId if there is no
        // current selection or the previously-selected board is no longer in the
        // refreshed list (e.g. it was deleted). This prevents polling/subscription
        // refreshes from resetting the user's active board to the first item.
        const currentId = state.activeBoardId.value
        const stillExists = currentId !== null && freshBoards.some((b) => b.id === currentId)
        if (!stillExists) {
          state.activeBoardId.value = freshBoards[0]?.id ?? null
        }
      } catch (e: unknown) {
        // A superseded read reports nothing, exactly as the detail path treats
        // a stale generation: raising the signed-out account's failure would
        // write an error surface into the state the reset just cleared.
        if (!isCurrentListGeneration()) {
          return
        }
        helpers.handleApiError(e, 'Failed to fetch boards')
        throw e
      } finally {
        state.loading.value = false
      }
    })()

    if (!isFilteredRequest) {
      unfilteredFetchBoardsInFlight = request
      const clearShareIfCurrent = () => {
        // Only the request that owns the slot may clear it, so a reset or a
        // newer request that already replaced it is left intact.  Clearing on
        // rejection is what lets a retry start a fresh request.
        if (unfilteredFetchBoardsInFlight === request) {
          unfilteredFetchBoardsInFlight = null
        }
      }
      void request.then(clearShareIfCurrent, clearShareIfCurrent)
    }

    return request
  }

  function settleQueuedBackgroundBoardFetch(committed = false) {
    const queued = queuedBackgroundBoardFetch
    queuedBackgroundBoardFetch = null
    queued?.resolve(committed)
  }

  function queueBackgroundBoardFetch(id: string): Promise<boolean> {
    if (queuedBackgroundBoardFetch?.boardId === id) {
      return queuedBackgroundBoardFetch.promise
    }

    settleQueuedBackgroundBoardFetch()
    let resolve!: (committed: boolean) => void
    const promise = new Promise<boolean>((innerResolve) => {
      resolve = innerResolve
    })
    queuedBackgroundBoardFetch = { boardId: id, promise, resolve }
    return promise
  }

  // Runs after any detail read settles: the queue holds either a background
  // refresh that waited behind an explicit load, or the successor of a read
  // that a local mutation invalidated.  Both are single-slot, so at most one
  // read follows and none runs in parallel with the request that queued it.
  function drainQueuedBackgroundBoardFetch(completedFetch: ActiveBoardFetch) {
    const queued = queuedBackgroundBoardFetch
    if (!queued || queued.boardId !== completedFetch.boardId) {
      return
    }

    queuedBackgroundBoardFetch = null
    void startBoardFetch(queued.boardId, 'background').then(queued.resolve, () => {
      queued.resolve(false)
    })
  }

  function cancelBackgroundBoardFetch(boardId?: string) {
    if (
      queuedBackgroundBoardFetch &&
      (boardId === undefined || queuedBackgroundBoardFetch.boardId === boardId)
    ) {
      settleQueuedBackgroundBoardFetch()
    }

    const active = activeBoardFetch
    if (
      active?.intent === 'background' &&
      (boardId === undefined || active.boardId === boardId)
    ) {
      boardFetchGeneration++
      active.controller.abort()
      if (activeBoardFetch === active) {
        activeBoardFetch = null
      }
    }
  }

  function fetchBoard(id: string, options: BoardFetchOptions = {}): Promise<boolean> {
    const intent = options.intent ?? 'explicit'

    if (intent === 'background' && activeBoardFetch) {
      if (activeBoardFetch.boardId !== id) {
        return Promise.resolve(false)
      }

      if (activeBoardFetch.intent === 'explicit') {
        return queueBackgroundBoardFetch(id)
      }

      return activeBoardFetch.promise
    }

    if (intent === 'explicit') {
      // A route load or Retry includes all mutations observed before it began,
      // so it supersedes any older queued background refresh.
      settleQueuedBackgroundBoardFetch()
    }

    return startBoardFetch(id, intent)
  }

  function startBoardFetch(id: string, intent: BoardFetchIntent): Promise<boolean> {
    const requestGeneration = ++boardFetchGeneration
    activeBoardFetch?.controller.abort()
    const controller = new AbortController()
    const mutationEpoch = helpers.getBoardDetailMutationEpoch(id)
    const request = {
      boardId: id,
      intent,
      generation: requestGeneration,
      controller,
      promise: Promise.resolve(false),
    } satisfies ActiveBoardFetch

    const isCurrentGeneration = () => requestGeneration === boardFetchGeneration
    const isCommitEligible = () =>
      isCurrentGeneration() && helpers.getBoardDetailMutationEpoch(id) === mutationEpoch

    // A payload that predates a completed local mutation is dropped rather than
    // committed, which leaves server-side effects the local patch cannot
    // describe — sibling reordering after a move — unrepaired.  The initiating
    // client receives its own realtime event before the mutation response, so
    // waiting for another event can strand that stale ordering.  Queue exactly
    // one successor read instead.  The bound holds through two mechanisms: a
    // background request that arrives while this read is open joins its promise
    // rather than starting a fan-out (see `fetchBoard`), and this check runs at
    // most once per read, so however many mutation events land during the
    // window they produce a single successor and never a parallel read.  The
    // queue is single-slot in any case.  Explicit reads are excluded: their
    // outcome is owned by the view that started them.
    const queueSuccessorForInvalidatedRead = () => {
      if (intent !== 'background' || !isCurrentGeneration()) {
        return
      }

      void queueBackgroundBoardFetch(id)
    }

    const performFetch = async (): Promise<boolean> => {
      if (helpers.isDemoMode) {
        if (intent === 'explicit') {
          state.loading.value = true
          state.error.value = null
        }
        const demo = buildDemoBoardDetail(id)
        if (!isCommitEligible()) {
          return false
        }

        state.currentBoard.value = demo.board
        state.currentBoardCards.value = demo.cards
        state.currentBoardLabels.value = []
        state.cardCommentsByCardId.value = {}
        if (intent === 'explicit') {
          state.loading.value = false
        }
        return true
      }

      try {
        if (intent === 'explicit') {
          state.loading.value = true
          state.error.value = null
        }
        const readOptions: BoardReadOptions = {
          signal: controller.signal,
          timeout: BOARD_REQUEST_TIMEOUT_MS,
          skipRetry: true,
        }
        const [board, cards, labels] = await Promise.all([
          boardsApi.getBoard(id, readOptions),
          cardsApi.getCards(id, undefined, readOptions),
          labelsApi.getLabels(id, readOptions),
        ])

        if (!isCommitEligible()) {
          queueSuccessorForInvalidatedRead()
          return false
        }

        const cardCounts = cards.reduce((counts, card) => {
          counts.set(card.columnId, (counts.get(card.columnId) ?? 0) + 1)
          return counts
        }, new Map<string, number>())
        board.columns.forEach((column) => {
          column.cardCount = cardCounts.get(column.id) ?? 0
        })

        state.currentBoard.value = board
        state.currentBoardCards.value = cards
        state.currentBoardLabels.value = labels
        state.cardCommentsByCardId.value = {}
        return true
      } catch (e: unknown) {
        // Ensure held-open siblings are cancelled before exposing a current
        // explicit failure or silently dropping stale/background work.
        controller.abort()
        // Request generation and cancellation are the only gates on a failure
        // outcome: a superseded or cancelled read reports nothing.
        if (!isCurrentGeneration() || axios.isCancel(e)) {
          return false
        }

        if (intent === 'background') {
          // Authorization freshness is a separate rule from board-payload
          // commits.  A completed local mutation advances the data epoch, which
          // only says this payload is too old to install; it says nothing about
          // whether the reader still has access.  So a 403 from a current,
          // uncancelled read stays authoritative even when the epoch moved
          // while it was in flight (#2435).
          const status = (e as { response?: { status?: number } } | null)?.response?.status
          if (status === 403) {
            helpers.handleApiError(
              new Error(BOARD_ACCESS_REVOKED_MESSAGE),
              BOARD_ACCESS_REVOKED_MESSAGE,
            )
          }
          return false
        }

        // Explicit failures stay epoch-gated at every status, 403 included: the
        // view that started the read owns its outcome and its error surface
        // (#2434), so a load a local write already superseded must not raise a
        // failure over state the user just changed successfully.  #2435 scopes
        // the authorization carve-out above to the background 403 only.
        if (!isCommitEligible()) {
          return false
        }

        helpers.handleApiError(e, 'Failed to fetch board')
        throw e
      } finally {
        if (intent === 'explicit' && isCurrentGeneration()) {
          state.loading.value = false
        }
      }
    }

    const promise = performFetch().finally(() => {
      if (activeBoardFetch !== request) {
        return
      }

      activeBoardFetch = null
      drainQueuedBackgroundBoardFetch(request)
    })
    request.promise = promise
    activeBoardFetch = request
    return promise
  }

  async function createBoard(board: CreateBoardDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const newBoard = await boardsApi.createBoard(board)
      state.boards.value.push(newBoard)
      helpers.toast.success(`Board "${newBoard.name}" created successfully`)
      return newBoard
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to create board')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function updateBoard(boardId: string, board: UpdateBoardDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const updatedBoard = await boardsApi.updateBoard(boardId, board)
      // Board settings are part of the board-detail fan-out, so a detail read
      // that captured the pre-save state must not replace this update (#2435).
      helpers.markBoardDetailMutation(boardId)

      // Update in boards list
      const index = state.boards.value.findIndex((b) => b.id === boardId)
      if (index !== -1) {
        state.boards.value[index] = updatedBoard
      }

      // Update current board if it's the one being edited
      if (state.currentBoard.value?.id === boardId) {
        state.currentBoard.value = { ...state.currentBoard.value, ...updatedBoard }
      }

      helpers.toast.success('Board updated successfully')
      return updatedBoard
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to update board')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function deleteBoard(boardId: string) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      await boardsApi.deleteBoard(boardId)

      // Clear detailed state for the current board before removing it from the
      // main boards list. This prevents any watchers on the `boards` array
      // from accidentally accessing stale detail state (like cards, labels, etc.)
      // that belongs to the board being deleted. The primary performance fix
      // for #519 is unmounting the BoardView before this action is called.
      const isCurrent = state.currentBoard.value?.id === boardId
      if (isCurrent) {
        state.currentBoard.value = null
        state.currentBoardCards.value = []
        state.currentBoardLabels.value = []
        state.cardCommentsByCardId.value = {}
        state.boardPresenceMembers.value = []
        state.editingCardId.value = null
      }

      // Remove from boards list after clearing detail state so downstream
      // watchers on `boards` do not attempt to read stale detail refs.
      state.boards.value = state.boards.value.filter((b) => b.id !== boardId)

      // Clear activeBoardId if the deleted board was the active selection
      if (state.activeBoardId.value === boardId) {
        state.activeBoardId.value = state.boards.value[0]?.id ?? null
      }

      helpers.toast.success('Board archived successfully')
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to archive board')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  /**
   * Clears every board value and every board request that belonged to the
   * session that just ended.
   *
   * The trigger is the shell's existing authenticated true-to-false
   * transition, the same one that already resets the workspace and capture
   * stores.  Nothing here is keyed on a user id or a token, so a routine token
   * refresh and a session restore do not reach it: those change identity
   * without ending the session, and clearing on them would drop the board the
   * user is looking at.
   *
   * Two generations are bumped rather than one because the list and the detail
   * read are separate lifecycles.  A bumped generation is what makes an
   * already-issued request safe: the response still arrives, finds its
   * generation stale, and returns without writing state, a throttle stamp, or
   * an error surface.  Clearing the state alone would not do it — a read that
   * was in flight during the reset would land afterwards and repopulate the
   * store with the previous account's boards.
   */
  function resetForLogout() {
    boardListGeneration++
    unfilteredFetchBoardsInFlight = null
    lastFetchBoardsAt = 0

    // The queued background successor is settled through the existing helper;
    // the active read is aborted at any intent, which the intent-scoped
    // cancelBackgroundBoardFetch deliberately does not do.
    boardFetchGeneration++
    activeBoardFetch?.controller.abort()
    activeBoardFetch = null
    settleQueuedBackgroundBoardFetch()

    const initialFilters: CardFilters = {
      searchText: '',
      labelIds: [],
      dueDateFilter: 'all',
      showBlockedOnly: false,
    }

    state.boards.value = []
    state.activeBoardId.value = null
    state.currentBoard.value = null
    state.currentBoardCards.value = []
    state.currentBoardLabels.value = []
    state.cardCommentsByCardId.value = {}
    state.boardPresenceMembers.value = []
    state.editingCardId.value = null
    state.loading.value = false
    state.error.value = null
    state.filters.value = initialFilters
  }

  return {
    fetchBoards,
    fetchBoard,
    cancelBackgroundBoardFetch,
    createBoard,
    updateBoard,
    deleteBoard,
    resetForLogout,
  }
}
