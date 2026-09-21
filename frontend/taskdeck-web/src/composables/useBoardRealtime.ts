import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr'
import type { BoardPresenceSnapshot, BoardRealtimeEvent } from '../types/realtime'
import { getToken } from '../utils/tokenStorage'
import { logWarn } from '../utils/errorReporting'
import { apiRootFrom } from '../utils/apiRoot'
import { isDemoMode } from '../utils/demoMode'
import { resolveApiBaseUrl } from '../utils/apiBaseUrl'

const BOARD_MUTATION_EVENT = 'boardMutation'
const BOARD_PRESENCE_EVENT = 'boardPresence'
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

export interface BoardRealtimeControllerOptions {
  fetchBoard: (boardId: string, options: { intent: 'background' }) => Promise<void>
  onPresenceChanged?: (snapshot: BoardPresenceSnapshot) => void
}

export interface BoardRealtimeController {
  start: (boardId: string) => Promise<void>
  switchBoard: (boardId: string) => Promise<void>
  setEditingCard: (cardId: string | null) => Promise<void>
  stop: () => Promise<void>
}

export function createBoardRealtimeController(
  options: BoardRealtimeControllerOptions,
): BoardRealtimeController {
  let connection: HubConnection | null = null
  let subscribedBoardId: string | null = null
  let requestedBoardId: string | null = null
  let subscriptionGeneration = 0
  let subscriptionTransition: Promise<void> = Promise.resolve()
  let editingCardId: string | null = null
  let fallbackTimer: ReturnType<typeof setInterval> | null = null
  let recoveryPending = false
  let refreshInFlight = false
  let pendingRefreshBoardId: string | null = null
  let mutationDebounceTimer: ReturnType<typeof setTimeout> | null = null

  const stopFallbackPolling = () => {
    if (!fallbackTimer) {
      return
    }

    clearInterval(fallbackTimer)
    fallbackTimer = null
  }

  const startFallbackPolling = (boardId: string) => {
    recoveryPending = true
    stopFallbackPolling()
    fallbackTimer = setInterval(() => {
      if (requestedBoardId !== boardId) {
        return
      }

      // Polling shares the same active/read-after-active slot as mutation
      // and recovery reads. Store-level deduplication must not swallow catch-up.
      startBoardRefresh(boardId)
    }, FALLBACK_POLL_INTERVAL_MS)
  }

  const cancelMutationDebounce = () => {
    if (mutationDebounceTimer !== null) {
      clearTimeout(mutationDebounceTimer)
      mutationDebounceTimer = null
    }
  }

  const startBoardRefresh = (boardId: string) => {
    // Fallback must also read a requested board whose hub join is pending.
    // Event handlers separately verify the confirmed subscription.
    if (requestedBoardId !== boardId) {
      return
    }

    if (refreshInFlight) {
      pendingRefreshBoardId = boardId
      return
    }

    refreshInFlight = true
    void options
      .fetchBoard(boardId, { intent: 'background' })
      .catch(() => {
        // Background refresh failures must not escape the realtime loop.
      })
      .finally(() => {
        refreshInFlight = false
        const pendingBoardId = pendingRefreshBoardId
        pendingRefreshBoardId = null

        if (pendingBoardId) {
          startBoardRefresh(pendingBoardId)
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
    hubConnection.onreconnecting(() => {
      if (connection === hubConnection && requestedBoardId) {
        startFallbackPolling(requestedBoardId)
      }
    })
    hubConnection.onreconnected(async () => {
      const boardId = requestedBoardId
      const generation = subscriptionGeneration
      const isCurrentRequest = () =>
        connection === hubConnection &&
        requestedBoardId === boardId &&
        subscriptionGeneration === generation
      if (!boardId || connection !== hubConnection) return
      recoveryPending = true

      // Transport recovery alone does not prove a board subscription. Keep
      // polling until JoinBoard acknowledges this request's generation.
      try {
        await queueBoardSubscription(boardId, generation)
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
        startFallbackPolling(requestedBoardId)
      }
    })

    connection = hubConnection
    return hubConnection
  }

  const joinBoard = async (boardId: string, generation: number) => {
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

    await hubConnection.invoke('JoinBoard', boardId)
    if (connection !== hubConnection) return
    subscribedBoardId = boardId
    if (isCurrentRequest() && hubConnection.state === HubConnectionState.Connected) {
      stopFallbackPolling()
      if (recoveryPending) {
        recoveryPending = false
        startBoardRefresh(boardId)
      }
    }
  }

  const queueBoardSubscription = (boardId: string, generation: number) => {
    const transition = subscriptionTransition
      .catch(() => undefined)
      .then(() => joinBoard(boardId, generation))
    subscriptionTransition = transition
    return transition
  }

  const requestBoardSubscription = (boardId: string) => {
    requestedBoardId = boardId
    const generation = ++subscriptionGeneration

    // Cancel any debounced mutation fetch from the previous board as soon as
    // navigation changes intent, rather than waiting for the connection move.
    cancelMutationDebounce()
    pendingRefreshBoardId = null
    // A switch while the old rejoin is pending inherits both recovery and
    // polling. Only this latest request's successful join may retire them.
    if (recoveryPending) startFallbackPolling(boardId)
    return queueBoardSubscription(boardId, generation)
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

  const stop = async () => {
    requestedBoardId = null
    subscriptionGeneration++
    recoveryPending = false
    stopFallbackPolling()
    cancelMutationDebounce()
    pendingRefreshBoardId = null
    editingCardId = null

    if (!connection) {
      subscribedBoardId = null
      return
    }

    try {
      if (subscribedBoardId && connection.state === HubConnectionState.Connected) {
        await connection.invoke('LeaveBoard', subscribedBoardId)
      }
    } catch {
      // Best-effort leave.
    }

    try {
      if (connection.state !== HubConnectionState.Disconnected) {
        await connection.stop()
      }
    } finally {
      subscribedBoardId = null
      connection = null
    }
  }

  return {
    start,
    switchBoard,
    setEditingCard,
    stop,
  }
}
