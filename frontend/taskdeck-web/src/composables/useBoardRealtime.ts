import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr'
import type { BoardPresenceSnapshot, BoardRealtimeEvent } from '../types/realtime'
import { getObservedCredentialGeneration, getToken } from '../utils/tokenStorage'
import { logWarn } from '../utils/errorReporting'
import { apiRootFrom } from '../utils/apiRoot'
import { isDemoMode } from '../utils/demoMode'
import { resolveApiBaseUrl } from '../utils/apiBaseUrl'

const BOARD_MUTATION_EVENT = 'boardMutation'
const BOARD_PRESENCE_EVENT = 'boardPresence'
const BOARD_ACCESS_REVOKED_EVENT = 'accessRevoked'
const RECONNECT_DELAYS_MS = [0, 2000, 5000, 10000]
const FALLBACK_POLL_INTERVAL_MS = 30000
// Coalesce rapid burst events so the board is not re-fetched on every
// individual mutation when multiple events arrive in quick succession (e.g.
// bulk import, automation runs).  300 ms is imperceptible to users but
// prevents the ~3 req/s thrash observed with rapid SignalR event bursts.
const MUTATION_DEBOUNCE_MS = 300

export function resolveHubUrl(): string {
  const apiRoot = apiRootFrom(resolveApiBaseUrl())
  return `${apiRoot}/hubs/boards`
}

function getAccessToken(): string {
  return getToken() ?? ''
}

/**
 * Whether a JoinBoard refusal is the server's authoritative access verdict.
 * BoardsHub.JoinBoard answers a failed reader check with
 * `HubException("Forbidden:...")`, which SignalR surfaces either as the bare
 * message or in a HubException-prefixed completion error. Only that code may retire the board:
 * transport failures, Unauthorized, and NotFound keep the polling fallback.
 */
function isJoinBoardForbiddenError(error: unknown): boolean {
  const message =
    typeof error === 'string' ? error : (error as { message?: unknown } | null)?.message
  return typeof message === 'string' &&
    /(?:^|HubException: )Forbidden(?::|$)/.test(message)
}

export interface BoardRealtimeControllerOptions {
  fetchBoard: (
    boardId: string,
    options: { intent: 'background'; afterActive?: boolean },
  ) => Promise<boolean>
  onPresenceChanged?: (snapshot: BoardPresenceSnapshot) => void
  onAccessRevoked?: (boardId: string) => void
}

export interface BoardRealtimeController {
  start: (boardId: string) => Promise<void>
  switchBoard: (boardId: string) => Promise<void>
  setEditingCard: (cardId: string | null) => Promise<void>
  stop: () => Promise<void>
  notifyAccessRevoked: (boardId: string) => void
}

export function createBoardRealtimeController(
  options: BoardRealtimeControllerOptions,
): BoardRealtimeController {
  let connection: HubConnection | null = null
  let subscribedBoardId: string | null = null
  let requestedBoardId: string | null = null
  let requestedCredentialGeneration: number | null = null
  let subscriptionGeneration = 0
  let subscriptionTransition: Promise<void> = Promise.resolve()
  let editingCardId: string | null = null
  let fallbackTimer: ReturnType<typeof setInterval> | null = null
  let recoveryPending = false
  let recoveryGeneration = 0
  let refreshInFlight = false
  let pendingRefreshBoardId: string | null = null
  let pendingRefreshAfterActive = false
  let pendingRecoveryRefresh = false
  let pendingRecoveryGeneration: number | null = null
  let mutationDebounceTimer: ReturnType<typeof setTimeout> | null = null

  const stopFallbackPolling = () => {
    if (!fallbackTimer) {
      return
    }

    clearInterval(fallbackTimer)
    fallbackTimer = null
  }

  const startFallbackPolling = (
    boardId: string,
    canDischargeRecovery = false,
    recoveryGenerationForRefresh = recoveryGeneration,
  ) => {
    recoveryPending = true
    stopFallbackPolling()
    const timerCanDischargeRecovery = canDischargeRecovery
    const timerRecoveryGeneration = recoveryGenerationForRefresh
    fallbackTimer = setInterval(() => {
      if (requestedBoardId !== boardId) {
        return
      }

      // Polling shares the same active/read-after-active slot as mutation
      // and recovery reads. Store-level deduplication must not swallow catch-up.
      startBoardRefresh(boardId, false, timerCanDischargeRecovery, timerRecoveryGeneration)
    }, FALLBACK_POLL_INTERVAL_MS)
  }

  const cancelMutationDebounce = () => {
    if (mutationDebounceTimer !== null) {
      clearTimeout(mutationDebounceTimer)
      mutationDebounceTimer = null
    }
  }

  const startBoardRefresh = (
    boardId: string,
    afterActive = false,
    dischargesRecovery = false,
    recoveryGenerationForRefresh = recoveryGeneration,
  ) => {
    // Fallback must also read a requested board whose hub join is pending.
    // Event handlers separately verify the confirmed subscription.
    if (requestedBoardId !== boardId) {
      return
    }

    if (refreshInFlight) {
      pendingRefreshBoardId = boardId
      pendingRefreshAfterActive ||= afterActive
      if (dischargesRecovery) {
        pendingRecoveryRefresh = true
        pendingRecoveryGeneration = recoveryGenerationForRefresh
      }
      return
    }

    refreshInFlight = true
    void options
      .fetchBoard(boardId, { intent: 'background', ...(afterActive ? { afterActive: true } : {}) })
      .then((committed) => {
        if (
          !dischargesRecovery ||
          requestedBoardId !== boardId ||
          recoveryGeneration !== recoveryGenerationForRefresh
        ) {
          return
        }

        if (committed !== true) {
          // A handled background failure or stale adapter resolves without an
          // affirmative commit. Keep the recovery obligation alive and let
          // bounded fallback polling retry it instead of treating the read as
          // complete.
          recoveryPending = true
          startFallbackPolling(boardId, true, recoveryGenerationForRefresh)
          return
        }

        recoveryPending = false
        stopFallbackPolling()
      })
      .catch(() => {
        // Background refresh failures must not escape the realtime loop.
        if (
          dischargesRecovery &&
          requestedBoardId === boardId &&
          recoveryGeneration === recoveryGenerationForRefresh
        ) {
          recoveryPending = true
          startFallbackPolling(boardId, true, recoveryGenerationForRefresh)
        }
      })
      .finally(() => {
        refreshInFlight = false
        const pendingBoardId = pendingRefreshBoardId
        const pendingAfterActive = pendingRefreshAfterActive
        const pendingRecovery = pendingRecoveryRefresh
        const pendingRecoveryGenerationValue = pendingRecoveryGeneration
        pendingRefreshBoardId = null
        pendingRefreshAfterActive = false
        pendingRecoveryRefresh = false
        pendingRecoveryGeneration = null

        if (pendingBoardId) {
          startBoardRefresh(
            pendingBoardId,
            pendingAfterActive,
            pendingRecovery,
            pendingRecoveryGenerationValue ?? recoveryGeneration,
          )
        }
      })
  }

  const handleBoardMutation = (event: BoardRealtimeEvent) => {
    if (
      !subscribedBoardId ||
      event.boardId !== subscribedBoardId ||
      event.boardId !== requestedBoardId
    ) {
      return
    }

    // Debounce: cancel any pending refresh scheduled by a prior burst event.
    cancelMutationDebounce()

    mutationDebounceTimer = setTimeout(() => {
      mutationDebounceTimer = null

      if (!subscribedBoardId || subscribedBoardId !== requestedBoardId) {
        return
      }

      startBoardRefresh(subscribedBoardId)
    }, MUTATION_DEBOUNCE_MS)
  }

  const handleBoardPresence = (snapshot: BoardPresenceSnapshot) => {
    if (
      !subscribedBoardId ||
      snapshot.boardId !== subscribedBoardId ||
      snapshot.boardId !== requestedBoardId
    ) {
      return
    }

    options.onPresenceChanged?.(snapshot)
  }

  const handleBoardAccessRevoked = (event: { boardId: string }) => {
    if (
      !subscribedBoardId ||
      event.boardId !== subscribedBoardId ||
      event.boardId !== requestedBoardId
    ) {
      return
    }

    retireForRevocation(event.boardId)
  }

  const ensureConnection = () => {
    if (connection) {
      return connection
    }

    const hubConnection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl(), {
        accessTokenFactory: getAccessToken,
        transport: HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect(RECONNECT_DELAYS_MS)
      .configureLogging(LogLevel.Warning)
      .build()

    hubConnection.on(BOARD_MUTATION_EVENT, handleBoardMutation)
    hubConnection.on(BOARD_PRESENCE_EVENT, handleBoardPresence)
    hubConnection.on(BOARD_ACCESS_REVOKED_EVENT, handleBoardAccessRevoked)
    hubConnection.onreconnecting(() => {
      if (connection === hubConnection && requestedBoardId) {
        recoveryGeneration += 1
        startFallbackPolling(requestedBoardId, false, recoveryGeneration)
      }
    })
    hubConnection.onreconnected(async () => {
      const boardId = requestedBoardId
      const generation = subscriptionGeneration
      const recoveryGenerationAtStart = recoveryGeneration
      const isCurrentRequest = () =>
        connection === hubConnection &&
        requestedBoardId === boardId &&
        subscriptionGeneration === generation
      if (!boardId || connection !== hubConnection) return
      recoveryPending = true

      // Transport recovery alone does not prove a board subscription. Keep
      // polling until JoinBoard acknowledges this request's generation.
      try {
        await queueBoardSubscription(boardId, generation, recoveryGenerationAtStart)
      } catch (error) {
        if (isCurrentRequest()) {
          logWarn('SignalR board rejoin failed, retaining polling fallback.', error)
          startFallbackPolling(boardId)
        }
        return
      }
      if (!isCurrentRequest() || hubConnection.state !== HubConnectionState.Connected) return

      // joinBoard owns catch-up so navigation during this await transfers
      // the recovery obligation to the latest queued board.
      if (editingCardId !== null) {
        try {
          await hubConnection.invoke('SetEditingCard', boardId, editingCardId)
        } catch (error) {
          if (isCurrentRequest()) {
            logWarn('SignalR editing presence could not be restored.', error)
          }
        }
      }
    })
    hubConnection.onclose(() => {
      if (connection === hubConnection && requestedBoardId) {
        recoveryGeneration += 1
        startFallbackPolling(requestedBoardId, false, recoveryGeneration)
      }
    })

    connection = hubConnection
    return hubConnection
  }

  const joinBoard = async (
    boardId: string,
    generation: number,
    recoveryGenerationForJoin = recoveryGeneration,
  ) => {
    const hubConnection = ensureConnection()
    const isCurrentRequest = () =>
      requestedBoardId === boardId && subscriptionGeneration === generation

    if (!isCurrentRequest()) {
      return
    }

    if (hubConnection.state === HubConnectionState.Disconnected) {
      try {
        await hubConnection.start()
      } catch (error) {
        if (!isCurrentRequest()) {
          return
        }

        logWarn('SignalR board realtime unavailable, using polling fallback.', error)
        startFallbackPolling(boardId)
        subscribedBoardId = boardId
        return
      }
    }

    if (!isCurrentRequest()) {
      return
    }

    // SignalR only accepts hub invocations when connected. During any
    // transition state (especially Reconnecting), retain the last confirmed
    // subscription so onreconnected can leave it and join the latest request.
    if (hubConnection.state !== HubConnectionState.Connected) {
      startFallbackPolling(boardId)
      return
    }

    if (subscribedBoardId && subscribedBoardId !== boardId) {
      await hubConnection.invoke('LeaveBoard', subscribedBoardId)
    }

    if (!isCurrentRequest()) {
      return
    }

    try {
      await hubConnection.invoke('JoinBoard', boardId)
    } catch (error) {
      // An authoritative Forbidden for the current request is revocation while
      // disconnected, not a transport failure: retire instead of polling.
      // Stale requests (navigated away, session changed) and every other
      // failure keep the existing polling fallback via the caller.
      if (
        isCurrentRequest() &&
        isJoinBoardForbiddenError(error) &&
        isCurrentSessionForRequest()
      ) {
        retireForRevocation(boardId)
        return
      }
      throw error
    }
    if (connection !== hubConnection) return
    subscribedBoardId = boardId
    if (isCurrentRequest() && hubConnection.state === HubConnectionState.Connected) {
      const ownsRecovery = recoveryPending && recoveryGenerationForJoin === recoveryGeneration
      if (!recoveryPending || ownsRecovery) {
        stopFallbackPolling()
      }
      if (ownsRecovery) {
        // Force the store to queue this read behind any active external
        // background fetch. The recovery obligation is discharged only when
        // this post-join read reports success.
        startBoardRefresh(boardId, true, true, recoveryGenerationForJoin)
      }
    }
  }

  const queueBoardSubscription = (
    boardId: string,
    generation: number,
    recoveryGenerationForJoin = recoveryGeneration,
  ) => {
    const transition = subscriptionTransition
      .catch(() => undefined)
      .then(() => joinBoard(boardId, generation, recoveryGenerationForJoin))
    subscriptionTransition = transition
    return transition
  }

  const requestBoardSubscription = (boardId: string) => {
    requestedBoardId = boardId
    // Snapshot session ownership alongside board intent: a Forbidden that
    // settles after a logout/login belongs to the previous session and must
    // not retire the board the new session just requested.
    getToken()
    requestedCredentialGeneration = getObservedCredentialGeneration()
    const generation = ++subscriptionGeneration

    // Cancel any debounced mutation fetch from the previous board as soon as
    // navigation changes intent, rather than waiting for the connection move.
    cancelMutationDebounce()
    pendingRefreshBoardId = null
    pendingRefreshAfterActive = false
    pendingRecoveryRefresh = false
    pendingRecoveryGeneration = null
    // A switch while the old rejoin is pending inherits both recovery and
    // polling. Only this latest request's successful join may retire them.
    if (recoveryPending) startFallbackPolling(boardId)
    return queueBoardSubscription(boardId, generation, recoveryGeneration)
  }

  const start = async (boardId: string) => {
    if (isDemoMode) return
    await requestBoardSubscription(boardId)
  }

  const switchBoard = async (boardId: string) => {
    if (isDemoMode) return
    await requestBoardSubscription(boardId)
  }

  const setEditingCard = async (cardId: string | null) => {
    editingCardId = cardId

    if (!connection || !subscribedBoardId || connection.state !== HubConnectionState.Connected) {
      return
    }

    await connection.invoke('SetEditingCard', subscribedBoardId, cardId)
  }

  const isCurrentSessionForRequest = () => {
    if (requestedCredentialGeneration === null) {
      return false
    }

    // Observe cross-tab/storage changes before comparing, as api/http.ts does.
    getToken()
    return getObservedCredentialGeneration() === requestedCredentialGeneration
  }

  const retireForRevocation = (boardId: string) => {
    // stop() retires refresh and polling intent before its first await. Report
    // the lost access immediately: a best-effort LeaveBoard can take time or
    // fail after the server has already evicted this connection.
    void stop().catch((error) => {
      logWarn('SignalR board teardown after access revocation failed.', error)
    })
    options.onAccessRevoked?.(boardId)
  }

  const notifyAccessRevoked = (boardId: string) => {
    // Out-of-band revocation (a fallback board read refused with 403): the
    // caller verified routing and session ownership. Re-check the requested
    // board so a result that crossed a switch cannot retire the new board.
    if (!boardId || requestedBoardId !== boardId) {
      return
    }

    retireForRevocation(boardId)
  }

  const stop = async () => {
    requestedBoardId = null
    requestedCredentialGeneration = null
    subscriptionGeneration++
    recoveryGeneration += 1
    recoveryPending = false
    stopFallbackPolling()
    cancelMutationDebounce()
    pendingRefreshBoardId = null
    pendingRefreshAfterActive = false
    pendingRecoveryRefresh = false
    pendingRecoveryGeneration = null
    editingCardId = null

    const hubConnection = connection
    const boardToLeave = subscribedBoardId
    connection = null
    subscribedBoardId = null
    if (!hubConnection) return

    try {
      if (boardToLeave && hubConnection.state === HubConnectionState.Connected) {
        await hubConnection.invoke('LeaveBoard', boardToLeave)
      }
    } catch {
      // Best-effort leave.
    }

    if (hubConnection.state !== HubConnectionState.Disconnected) {
      await hubConnection.stop()
    }
  }

  return {
    start,
    switchBoard,
    setEditingCard,
    stop,
    notifyAccessRevoked,
  }
}
