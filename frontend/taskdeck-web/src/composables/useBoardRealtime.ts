import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr'
import type { BoardPresenceSnapshot, BoardRealtimeEvent } from '../types/realtime'

const BOARD_MUTATION_EVENT = 'boardMutation'
const BOARD_PRESENCE_EVENT = 'boardPresence'
const RECONNECT_DELAYS_MS = [0, 2000, 5000, 10000]
const FALLBACK_POLL_INTERVAL_MS = 15000

function resolveHubUrl(): string {
  const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api'
  const apiRoot = apiBase.replace(/\/api\/?$/i, '')
  return `${apiRoot}/hubs/boards`
}

function getAccessToken(): string {
  return localStorage.getItem('taskdeck_token') ?? ''
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
  let editingCardId: string | null = null
  let fallbackTimer: ReturnType<typeof setInterval> | null = null
  let refreshInFlight = false

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
      void options.fetchBoard(boardId).catch(() => {
        // Keep fallback resilient; fetch failures are already surfaced by store-level handling.
      })
    }, FALLBACK_POLL_INTERVAL_MS)
  }

  const handleBoardMutation = async (event: BoardRealtimeEvent) => {
    if (!subscribedBoardId || event.boardId !== subscribedBoardId || refreshInFlight) {
      return
    }

    refreshInFlight = true
    try {
      await options.fetchBoard(subscribedBoardId)
    } finally {
      refreshInFlight = false
    }
  }

  const handleBoardPresence = (snapshot: BoardPresenceSnapshot) => {
    if (!subscribedBoardId || snapshot.boardId !== subscribedBoardId) {
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
      if (subscribedBoardId) {
        startFallbackPolling(subscribedBoardId)
      }
    })
    hubConnection.onreconnected(async () => {
      stopFallbackPolling()
      if (subscribedBoardId) {
        await hubConnection.invoke('JoinBoard', subscribedBoardId)
        if (editingCardId !== null) {
          await hubConnection.invoke('SetEditingCard', subscribedBoardId, editingCardId)
        }
      }
    })
    hubConnection.onclose(() => {
      if (subscribedBoardId) {
        startFallbackPolling(subscribedBoardId)
      }
    })

    connection = hubConnection
    return hubConnection
  }

  const joinBoard = async (boardId: string) => {
    const hubConnection = ensureConnection()

    if (hubConnection.state === HubConnectionState.Disconnected) {
      try {
        await hubConnection.start()
      } catch (error) {
        console.warn('SignalR board realtime unavailable, using polling fallback.', error)
        startFallbackPolling(boardId)
        subscribedBoardId = boardId
        return
      }
    }

    if (subscribedBoardId && subscribedBoardId !== boardId) {
      await hubConnection.invoke('LeaveBoard', subscribedBoardId)
    }

    await hubConnection.invoke('JoinBoard', boardId)
    subscribedBoardId = boardId
    stopFallbackPolling()
  }

  const start = async (boardId: string) => {
    await joinBoard(boardId)
  }

  const switchBoard = async (boardId: string) => {
    await joinBoard(boardId)
  }

  const setEditingCard = async (cardId: string | null) => {
    editingCardId = cardId

    if (!connection || !subscribedBoardId || connection.state !== HubConnectionState.Connected) {
      return
    }

    await connection.invoke('SetEditingCard', subscribedBoardId, cardId)
  }

  const stop = async () => {
    stopFallbackPolling()
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
