import { computed, onScopeDispose, ref, watch } from 'vue'
import { boardsApi } from '../api/boardsApi'
import { BOARD_REQUEST_TIMEOUT_MS } from '../api/http'
import { isDemoMode } from '../utils/demoMode'
import { useBoardStore } from '../store/boardStore'
import { useSessionStore } from '../store/sessionStore'

export interface UseCardTypePermissionOptions {
  getCardId?: () => string
  /** The board the open card belongs to. */
  getBoardId: () => string
  /** Whether the card editor is open; a closed editor asks the server nothing. */
  getIsOpen: () => boolean
  /**
   * The settled archive state of the open card (`CardModal`'s `cardIsArchived`). It decides
   * whether an unresolved permission is worth asking for, and whether the type may be edited;
   * it does not decide `canWrite`, which an archived card's Restore control still needs.
   */
  getCardIsArchived: () => boolean
}

/**
 * Server-authoritative board write permission for the open card editor (#2952, #3028).
 *
 * One read serves every write gate in the editor: the work-item type selector
 * (`canEditType`), and — through `canWrite` — the parent selector, the archive/restore
 * control and the assignment field's `readOnly` input. They asked the same question of the
 * same payload and three of them still read an omitted optional field as "no"; a second,
 * third and fourth request would answer nothing extra, so the answer is resolved once here
 * and passed down. The name is kept from its first consumer so the in-flight #3030 slice
 * keeps its file.
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
  const session = useSessionStore()
  const permissionRecovery = ref(false)
  const accessUnavailable = ref(false)
  /** The latest board-read request at the start of a denied-write recovery. */
  let recoveryRequestGeneration: number | null = null

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

  /*
   * Board detail reads retain both their request and committed-payload
   * generations. That makes a later `canWrite: true` evidence when a denied
   * write is being reconciled, while an optimistic/local object replacement or
   * a response already in flight at the denial is not.
   * Older test harnesses and alternate stores that do not carry the marker stay
   * on the existing explicit-read recovery path.
   */
  const boardPayloadGeneration = computed<number | null>(() =>
    typeof boardStore.currentBoardPayloadGeneration === 'number'
      ? boardStore.currentBoardPayloadGeneration
      : null,
  )
  const boardRequestGeneration = computed<number | null>(() =>
    typeof boardStore.currentBoardRequestGeneration === 'number'
      ? boardStore.currentBoardRequestGeneration
      : null,
  )

  const confirmedPermission = computed<boolean | null>(() =>
    confirmed.value && confirmed.value.boardId === options.getBoardId() ? confirmed.value.canWrite : null,
  )

  const permission = computed<boolean | null>(() => permissionRecovery.value
    ? confirmedPermission.value
    : statedPermission.value ?? confirmedPermission.value)

  /*
   * Whether this composable answers the permission question for the open card at all.
   * Demo mode has no server to ask (its board fixtures omit `canWrite` by construction, so
   * a read would be a request to a backend that is not there), and a card whose board
   * payload is not the loaded one is a deliberate exclusion rather than an impossibility:
   * that read COULD be made, but this slice keeps the pre-existing behaviour for it (#2952
   * is about the loaded board's missing field) rather than adding a request to every card
   * opened from a cross-board surface. In both cases the gates stay exactly as read-only as
   * they were before this composable existed.
   */
  const permissionDecides = computed(() => !isDemoMode && boardForCard.value !== null)

  /*
   * Whether an unresolved permission is worth a request and a recovery affordance.
   * An archived card cannot accept an edit, so #2952 spent no request on one and showed no
   * affordance that would promise a refresh changing nothing; that stands. It is deliberately
   * NOT part of `canWrite`: restoring an archived card is a write the archive control offers
   * ON an archived card, so a permission the payload already states must still reach it.
   * An explicit reconciliation after a denied write also runs for an archived card, so a
   * refused Restore can recover in place. The initial archived-card path is unchanged.
   */
  const readDecides = computed(() => permissionDecides.value &&
    (permissionRecovery.value || !options.getCardIsArchived()))

  /** Board-level write permission: what every write gate in the editor is allowed to assume. */
  const canWrite = computed(() => permissionDecides.value && permission.value === true)
  const canEditType = computed(() => canWrite.value && !options.getCardIsArchived())
  const permissionChecking = computed(() => readDecides.value && permission.value === null && checking.value)
  const permissionUnknown = computed(() => readDecides.value && permission.value === null && !checking.value)
  // A refused write never promises read access. A successful Viewer read can confirm it.
  const readsBlocked = computed(() => permissionRecovery.value &&
    (checking.value || accessUnavailable.value || confirmedPermission.value === null))

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
      // The legacy convention still applies on an initial missing-field read. After a
      // write403 contradicted it, require an explicit permission before granting again.
      if (permissionRecovery.value && board.isArchived !== true && typeof board.canWrite !== 'boolean') {
        confirmed.value = null
        failedBoardId.value = boardId
      } else {
        confirmed.value = { boardId, canWrite: board.canWrite !== false && board.isArchived !== true }
      }
      accessUnavailable.value = false
    } catch (cause) {
      // A failed read grants nothing. The unknown state stands and the caller offers the
      // explicit retry; the server still refuses any write this control should not allow.
      if (current !== generation) return
      confirmed.value = null
      failedBoardId.value = boardId
      const status = (cause as { response?: { status?: number } })?.response?.status
      accessUnavailable.value = status === 403 || status === 404
    } finally {
      if (current === generation) {
        inFlightBoardId = null
        inFlightRequest = null
        checking.value = false
      }
    }
  }

  // A closed or replaced editor is not waiting for an answer, and must not be given one.
  function cancelRead() {
    generation++
    inFlightRequest?.abort()
    inFlightRequest = null
    inFlightBoardId = null
    checking.value = false
  }
  onScopeDispose(cancelRead)

  async function beginPermissionRecovery(newDenial: boolean) {
    if (!options.getIsOpen() || !permissionDecides.value) return
    // A manual retry remains tied to the refusal it is reconciling. Keep that
    // boundary so a board-store read begun after the refusal remains fresh
    // evidence when the retry fails. A newly refused write advances it.
    permissionRecovery.value = true
    if (newDenial || recoveryRequestGeneration === null) {
      recoveryRequestGeneration = boardRequestGeneration.value
    }
    confirmed.value = null
    await read(options.getBoardId())
  }

  /** Explicit recovery from an unknown permission state. */
  async function refreshPermission() {
    await beginPermissionRecovery(false)
  }

  /** A newly refused write invalidates evidence that predated that refusal. */
  async function recoverFromPermissionDenied() {
    await beginPermissionRecovery(true)
  }

  watch(
    [() => options.getIsOpen(), () => options.getBoardId(), () => options.getCardId?.(), () => session.userId],
    ([_open, _board, _card, actor], previous) => {
      cancelRead()
      confirmed.value = null
      failedBoardId.value = null
      // An account change cannot inherit the previous caller's cached board permission.
      permissionRecovery.value = actor !== previous[3]
      recoveryRequestGeneration = null
      accessUnavailable.value = false
    },
    { flush: 'sync' },
  )

  watch([statedPermission, boardPayloadGeneration], ([value, payloadGeneration], [previousValue, previousPayloadGeneration]) => {
    if (!permissionRecovery.value || value === null) return

    // A direct false transition is safe to consume immediately: it can only
    // further restrict the editor. A later true needs the store's committed
    // server-payload marker, because a local patch must never re-authorize a
    // write the server just refused.
    const permissionWasRevoked = value === false && value !== previousValue
    const hasFreshServerPayload = payloadGeneration !== null &&
      payloadGeneration !== previousPayloadGeneration &&
      (recoveryRequestGeneration === null || payloadGeneration > recoveryRequestGeneration)
    if (!permissionWasRevoked && !hasFreshServerPayload) return

    // Retire any older reconciliation read rather than letting its delayed
    // answer reverse the newer server payload or restriction.
    cancelRead()
    confirmed.value = { boardId: options.getBoardId(), canWrite: value }
    failedBoardId.value = null
    accessUnavailable.value = false
  }, { flush: 'sync' })

  watch(
    [() => options.getIsOpen(), () => options.getBoardId(), permission, readDecides,
      () => options.getCardId?.(), () => session.userId],
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

  return { canWrite, canEditType, permissionChecking, permissionUnknown, permissionRecovery,
    accessUnavailable, readsBlocked, refreshPermission, recoverFromPermissionDenied }
}
