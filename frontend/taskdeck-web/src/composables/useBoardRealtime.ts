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

const BOARD_MUTATION_EVENT = 'boardMutation'
const BOARD_PRESENCE_EVENT = 'boardPresence'
const RECONNECT_DELAYS_MS = [0, 2000, 5000, 10000]
const FALLBACK_POLL_INTERVAL_MS = 30000
// Coalesce rapid burst events so the board is not re-fetched on every
// individual mutation when multiple events arrive in quick succession (e.g.
// bulk import, automation runs).  300 ms is imperceptible to users but
// prevents the ~3 req/s thrash observed with rapid SignalR event bursts.
const MUTATION_DEBOUNCE_MS = 300

function resolveHubUrl(): string {
  const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api'
  const apiRoot = apiBase.replace(/\/api\/?$/i, '')
  return `${apiRoot}/hubs/boards`
}

function getAccessToken(): string {
  return getToken() ?? ''
}

export interface BoardRealtimeControllerOptions {
  fetchBoard: (boardId: string) => Promise<void>
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
  let refreshInFlight = false
  let mutationDebounceTimer: ReturnType<typeof setTimeout> | null = null

  const stopFallbackPolling = () => {
    if (!fallbackTimer) {
      return
    }

    clearInterval(fallbackTimer)
    fallbackTimer = null
  }

  const startFallbackPolling = (boardId: string) => {
    stopFallbackPolling()
    fallbackTimer = setInterval(() => {
      if (requestedBoardId !== boardId) {
        return
      }

      void options.fetchBoard(boardId).catch(() => {
        // Keep fallback resilient; fetch failures are already surfaced by store-level handling.
      })
    }, FALLBACK_POLL_INTERVAL_MS)
  }

  const cancelMutationDebounce = () => {
    if (mutationDebounceTimer !== null) {
      clearTimeout(mutationDebounceTimer)
      mutationDebounceTimer = null
    }
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

      // Skip if a refresh is already in-flight (started by a previous debounced
      // call that hasn't resolved yet).
      if (refreshInFlight || !subscribedBoardId || subscribedBoardId !== requestedBoardId) {
        return
      }

      const boardId = subscribedBoardId
      refreshInFlight = true
      void options.fetchBoard(boardId).finally(() => {
        refreshInFlight = false
      })
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
      if (requestedBoardId) {
        startFallbackPolling(requestedBoardId)
      }
    })
    hubConnection.onreconnected(async () => {
      stopFallbackPolling()
      const boardId = requestedBoardId
      const generation = subscriptionGeneration
      if (boardId) {
        await queueBoardSubscription(boardId, generation)
        if (
          editingCardId !== null &&
          requestedBoardId === boardId &&
          subscriptionGeneration === generation
        ) {
          await hubConnection.invoke('SetEditingCard', boardId, editingCardId)
        }
      }
    })
    hubConnection.onclose(() => {
      if (requestedBoardId) {
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

    if (subscribedBoardId && subscribedBoardId !== boardId) {
      await hubConnection.invoke('LeaveBoard', subscribedBoardId)
    }

    if (!isCurrentRequest()) {
      return
    }

    await hubConnection.invoke('JoinBoard', boardId)
    subscribedBoardId = boardId
    stopFallbackPolling()
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
    return queueBoardSubscription(boardId, generation)
  }

  const start = async (boardId: string) => {
    await requestBoardSubscription(boardId)
  }

  const switchBoard = async (boardId: string) => {
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
    stopFallbackPolling()
    cancelMutationDebounce()
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
