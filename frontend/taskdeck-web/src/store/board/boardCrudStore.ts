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
import { initialCardFilters, type BoardState } from './boardState'
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

export interface BoardListFetchOptions {
  /**
   * Skip the throttle window, and NOTHING else — an explicit user request for
   * fresh data, which the 5 s gap between mounts was never meant to answer.
   *
   * The stamp is written only after a success, so a retry that follows a FAILED
   * list read was never blocked by it. What this exists for is the stamp an
   * EARLIER success left behind: `state.error` is shared by every board action,
   * so a create/rename/archive failure two seconds after a good list read puts
   * BoardsListView on its error branch with a Retry control, and without this
   * the click returned here before touching `loading` or issuing a request —
   * no skeleton, no request, a dead button until the window passed (#2689
   * round-2 finding 1).
   *
   * Deliberately NOT a bypass of the in-flight share: joining a read that is
   * already on the wire is the correct answer to a second caller, and forcing
   * a parallel one would reintroduce the #1961 fan-out the share removed.
   */
  force?: boolean
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
  // Every board-list read that has not settled yet, filtered or not.  The
  // detail path keeps a single `activeBoardFetch` because it supersedes itself;
  // list reads do not, so a filtered read can be open alongside the share and
  // both must be reachable by the logout abort.  Each request removes its own
  // controller when it settles.
  const inFlightBoardListReads = new Set<AbortController>()
  // The message the most recent list-read failure wrote into the shared
  // `state.error`, so a later list success can tell whether the alert on screen
  // is still ITS alert. See the success-path clear below.
  let lastListReadError: string | null = null
  let boardFetchGeneration = 0
  let activeBoardFetch: ActiveBoardFetch | null = null
  let queuedBackgroundBoardFetch: QueuedBackgroundBoardFetch | null = null

  async function fetchBoards(
    search?: string,
    includeArchived = false,
    options: BoardListFetchOptions = {},
  ) {
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
    // The share check above is deliberately ahead of this and is NOT skipped by
    // `force`; only the throttle window is. See BoardListFetchOptions.force.
    if (!options.force && !isFilteredRequest && now - lastFetchBoardsAt < FETCH_BOARDS_THROTTLE_MS) {
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
    const controller = new AbortController()
    inFlightBoardListReads.add(controller)

    const request = (async () => {
      try {
        state.loading.value = true
        state.error.value = null
        // Bounded exactly like the detail read below (`startBoardFetch`), and
        // for a reason the share made sharper: once every unfiltered caller in
        // the page joins one promise, a read that never settles pins all of
        // them until logout instead of failing one mount.  The axios instance
        // sets no default timeout, so without this an unanswered socket is
        // unbounded, and a 503 with `Retry-After` costs three waits of up to
        // MAX_DELAY_MS each (`httpRetry.ts`) before the caller hears anything.
        //
        // Filtered reads take the same bound rather than a weaker one.  They
        // hit the same endpoint, so a retry policy that changed with a query
        // parameter would be a trap for the next caller, and the only filtered
        // caller today (`useActivityQuery.loadSelectorData`) already delegates
        // its failure to this store's error surface rather than to the retry
        // layer.  `timeout` alone fixes the wedged socket; `skipRetry` is here
        // because of the share above: every unfiltered caller on the page
        // joins one promise, so a retry chain would not lengthen one mount's
        // skeleton but pin the boards list, the inbox composer, the triage
        // picker, Metrics and Saved Views together for up to four attempts
        // plus three `Retry-After` waits, the same page-wide stall #2685
        // exists to remove, only with a ceiling.  The cost is that a one-off
        // 503 no longer heals itself on surfaces without a Retry control
        // (tracked on the follow-up issue named in the PR).
        const freshBoards = await boardsApi.getBoards(search, includeArchived, {
          signal: controller.signal,
          timeout: BOARD_REQUEST_TIMEOUT_MS,
          skipRetry: true,
        })
        // A logout reset ran while this read was on the wire.  The payload
        // belongs to the account that is now signed out, so neither it nor the
        // throttle stamp it would write may reach the next session's store —
        // and dropping the stamp is what lets that session refetch at once.
        if (!isCurrentListGeneration()) {
          return
        }
        lastFetchBoardsAt = Date.now()
        state.boards.value = freshBoards
        // A current-generation success owns the error surface as well as the
        // list — but only the part of it a LIST READ wrote. Clearing at the
        // START of a read is not enough, because two list reads overlap: the
        // activity selector's `includeArchived` read never joins the share, so
        // it can still be on the wire when the boards list mounts an unfiltered
        // one. If the earlier of the pair fails after the later has cleared and
        // committed, `error` is left set beside a populated `boards`, and
        // BoardsListView's `v-if loading / v-else-if error / … / v-else grid`
        // chain shows the alert INSTEAD of the boards it already has (#2689
        // item 4). The bound made the failing half deterministic at 10 s, so it
        // stopped being a race nobody hits.
        //
        // The equality guard is what keeps that from overreaching. `error` is
        // one ref shared by every board action, so an UNCONDITIONAL clear here
        // erased alerts this read never raised: mount read slow, the user
        // submits the create form at T+2 s, createBoard fails and sets its
        // message, the mount read commits at T+5 s and wipes it — a failure the
        // user was told about and then silently was not (#2689 round-2 finding
        // 2). Same shape as the guarded clear in BoardView.vue: clear only the
        // error still identical to the one this path observed. The marker is
        // dropped on any current-generation success, matched or not, so a stale
        // message can never authorise a later clear.
        const listReadErrorToClear = lastListReadError
        lastListReadError = null
        if (listReadErrorToClear !== null && state.error.value === listReadErrorToClear) {
          state.error.value = null
        }

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
        // write an error surface into the state the reset just cleared.  The
        // cancellation arm is the same belt-and-braces the detail path carries:
        // the only abort today is the logout reset, which bumps the generation
        // first, so this arm is unreachable — it is here so a future aborter
        // that forgets the bump still cannot toast a cancellation at the user.
        if (!isCurrentListGeneration() || axios.isCancel(e)) {
          return
        }
        // Remember exactly what was written, post-translation, so the guard on
        // the success path above compares against the message actually on
        // screen rather than against the fallback.
        lastListReadError = helpers.handleApiError(e, 'Failed to fetch boards')
        throw e
      } finally {
        inFlightBoardListReads.delete(controller)
        // Gated for the same reason the detail path gates its own loading
        // write: by the time a superseded read settles, the flag belongs to
        // the read that replaced it.  Clearing it here would drop the next
        // session's skeleton and show that user an empty account until their
        // own read resolves.
        //
        // That makes the gate correct only while every bumper of
        // boardListGeneration also clears state.loading in the same synchronous
        // turn, so no read is left owning a flag nobody will clear.
        // resetForLogout is the only bumper today and does exactly that.  A
        // list-side cancel helper modelled on cancelBackgroundBoardFetch —
        // which bumps boardFetchGeneration and deliberately leaves the flag
        // alone — would strand loading true and leave BoardsListView on its
        // skeleton for good.  Clear the flag alongside any new bumper, or
        // replace this gate with a per-request ownership token that does not
        // depend on the coupling.
        if (isCurrentListGeneration()) {
          state.loading.value = false
        }
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
   * generation stale, and returns without writing board state, the loading
   * flag, a throttle stamp, or an error surface.  The loading flag is in that
   * list because it outlives the read that set it: once a newer read owns it,
   * a superseded response clearing it would strand the newer read's view in an
   * empty state.  Clearing the state alone would not do it — a read that was
   * in flight during the reset would land afterwards and repopulate the store
   * with the previous account's boards.
   *
   * The generation bump makes a late response harmless; the abort keeps it from
   * being sent at all, so no request outlives the session that started it.  Both
   * lifecycles are aborted: every open list read and the active detail read.
   */
  function resetForLogout() {
    boardListGeneration++
    // The bump comes first so the rejection each abort produces lands on a
    // stale generation: the catch returns before handleApiError, so no toast
    // follows the user into the login screen, and the finally gate leaves the
    // loading flag to whoever owns it next.  Reversing the two would happen to
    // work — axios rejects on a later microtask — but would rest on that
    // timing instead of on the guard.
    //
    // The flag is cleared below in this same synchronous turn, which is the
    // coupling the fetchBoards finally gate depends on; see the comment there
    // before adding another bumper of boardListGeneration.
    for (const controller of inFlightBoardListReads) {
      controller.abort()
    }
    inFlightBoardListReads.clear()
    unfilteredFetchBoardsInFlight = null
    lastFetchBoardsAt = 0
    // The previous session's alert is cleared below, so the marker that would
    // authorise clearing it must not outlive it either.
    lastListReadError = null

    // The queued background successor is settled through the existing helper;
    // the active read is aborted at any intent, which the intent-scoped
    // cancelBackgroundBoardFetch deliberately does not do.
    boardFetchGeneration++
    activeBoardFetch?.controller.abort()
    activeBoardFetch = null
    settleQueuedBackgroundBoardFetch()

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
    state.filters.value = initialCardFilters()
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
