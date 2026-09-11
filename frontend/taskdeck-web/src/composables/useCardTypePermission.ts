import { computed, onScopeDispose, ref, watch } from 'vue'
import { boardsApi } from '../api/boardsApi'
import { BOARD_REQUEST_TIMEOUT_MS } from '../api/http'
import { isDemoMode } from '../utils/demoMode'
import { useBoardStore } from '../store/boardStore'

export interface UseCardTypePermissionOptions {
  /** The board the open card belongs to. */
  getBoardId: () => string
  /** Whether the card editor is open; a closed editor asks the server nothing. */
  getIsOpen: () => boolean
  /** The settled archive state of the open card (`CardModal`'s `cardIsArchived`). */
  getCardIsArchived: () => boolean
}

/**
 * Server-authoritative write permission for the work-item type control (#2952).
 *
 * The loaded board payload carries `BoardDto.CanWrite`, the server's own answer for
 * the calling user — but the field is optional, and the `Board` contract in
 * `types/board.ts` says why: a payload cached before the field existed simply omits
 * it, and surfaces gating on it must treat ONLY an explicit `false` as read-only.
 * A strict `=== true` gate therefore reads that silence as "no", and an authorized
 * writer holding an older cached board finds the type selector disabled with nothing
 * to do about it.
 *
 * The answer here is evidence, not inference: when the loaded payload does not state
 * the permission, read the board back from the server once and gate on what that read
 * says. Client-derived ownership is never consulted — `permissionsStore.canEdit` reads
 * `BoardAccess` rows, which board owners do not have — and the write itself stays
 * server-authoritative regardless of what this control offers. When the read fails,
 * the state stays unknown and the caller offers an explicit "Refresh permission"
 * recovery instead of a silently disabled control.
 *
 * A payload that already states the permission costs no request at all, which is every
 * board loaded from the current server.
 */
export function useCardTypePermission(options: UseCardTypePermissionOptions) {
  const boardStore = useBoardStore()

  /** The permission this composable's own server read confirmed, scoped to its board. */
  const confirmed = ref<{ boardId: string; canWrite: boolean } | null>(null)
  const checking = ref(false)
  /** The board whose read failed; cleared when a retry starts. */
  const failedBoardId = ref<string | null>(null)
  // Only the newest read may apply: a board change (inspector switching cards across
  // boards) must not be answered by a response for the board before it.
  let generation = 0
  /** The board whose read is open right now, so a board change supersedes it rather than waiting. */
  let inFlightBoardId: string | null = null
  // A superseded read is not merely ignored, it is cancelled: the editor can be closed or
  // moved to another card long before a slow read answers, and an unanswered request that
  // nothing is waiting for should not stay open.
  let inFlightRequest: AbortController | null = null

  const boardForCard = computed(() => {
    const board = boardStore.currentBoard
    return board && board.id === options.getBoardId() ? board : null
  })

  /** What the loaded payload states, or `null` when it states nothing. */
  const statedPermission = computed<boolean | null>(() => {
    const board = boardForCard.value
    if (!board) return null
    // An archived board is read-only whatever the permission field says, and no read
    // can change that — so it is a stated `false`, not an unknown.
    if (board.isArchived === true) return false
    if (typeof board.canWrite !== 'boolean') return null
    return board.canWrite
  })

  const confirmedPermission = computed<boolean | null>(() =>
    confirmed.value && confirmed.value.boardId === options.getBoardId() ? confirmed.value.canWrite : null,
  )

  const permission = computed<boolean | null>(() => statedPermission.value ?? confirmedPermission.value)

  /*
   * Permission is only one of the reasons this control can be read-only, and it is the
   * only one this composable answers. An archived card cannot accept a type change at all,
   * and demo mode has no server to ask (its board fixtures omit `canWrite` by construction,
   * so a read would be a request to a backend that is not there) — so neither spends a
   * request, and neither shows the recovery affordance, which would promise a refresh that
   * changes nothing.
   *
   * A card whose board payload is not the loaded one is a deliberate exclusion rather than
   * an impossibility: that read COULD be made, but this slice keeps the pre-existing
   * behaviour for it (#2952 is about the loaded board's missing field) rather than adding a
   * request to every card opened from a cross-board surface.
   */
  const permissionDecides = computed(() =>
    !isDemoMode && boardForCard.value !== null && !options.getCardIsArchived(),
  )

  const canEditType = computed(() => permissionDecides.value && permission.value === true)
  const permissionChecking = computed(() => permissionDecides.value && permission.value === null && checking.value)
  const permissionUnknown = computed(() => permissionDecides.value && permission.value === null && !checking.value)

  async function read(boardId: string) {
    const current = ++generation
    inFlightRequest?.abort()
    const request = new AbortController()
    inFlightRequest = request
    inFlightBoardId = boardId
    checking.value = true
    failedBoardId.value = null
    try {
      /*
       * The same read discipline every other board read uses (`boardCrudStore`): bounded by
       * the board timeout, and NOT routed through the shared retry interceptor. A retried
       * read would hold this control in "checking" for the whole backoff — up to a minute
       * when a `Retry-After` is honoured — with the recovery affordance hidden behind it.
       * One bounded attempt, then the user decides whether to ask again.
       */
      const board = await boardsApi.getBoard(boardId, {
        signal: request.signal,
        timeout: BOARD_REQUEST_TIMEOUT_MS,
        skipRetry: true,
      })
      if (current !== generation) return
      /*
       * A FRESH payload that still omits the field is not a stale cache — it is a server
       * that predates the field, and the `Board` contract's legacy convention governs it:
       * only an explicit `false` is read-only. An archived board is read-only whatever the
       * field says.
       */
      confirmed.value = { boardId, canWrite: board.canWrite !== false && board.isArchived !== true }
    } catch {
      // A failed read grants nothing. The unknown state stands and the caller offers the
      // explicit retry; the server still refuses any write this control should not allow.
      if (current !== generation) return
      failedBoardId.value = boardId
    } finally {
      if (current === generation) {
        inFlightBoardId = null
        inFlightRequest = null
        checking.value = false
      }
    }
  }

  // A closed or replaced editor is not waiting for an answer, and must not be given one.
  onScopeDispose(() => {
    generation++
    inFlightRequest?.abort()
    inFlightRequest = null
    inFlightBoardId = null
  })

  /** Explicit recovery from an unknown permission state. */
  async function refreshPermission() {
    if (inFlightBoardId === options.getBoardId()) return
    await read(options.getBoardId())
  }

  watch(
    [() => options.getIsOpen(), () => options.getBoardId(), permission, permissionDecides],
    ([isOpen, boardId, currentPermission, decides]) => {
      if (!isOpen || !decides || currentPermission !== null) return
      // One automatic attempt per board: a second failure is the user's to ask for.
      if (failedBoardId.value === boardId) return
      // A read already open for THIS board answers it. A read open for the board this
      // editor just moved off does not, and must not be waited on: it is superseded.
      if (inFlightBoardId === boardId) return
      void read(boardId)
    },
    { immediate: true },
  )

  return { canEditType, permissionChecking, permissionUnknown, refreshPermission }
}
