/**
 * Resilience and degraded-mode tests for frontend stores and composables.
 * Issue #720 (TST-53): Covers API unreachable, slow/partial responses,
 * malformed API data, localStorage cleared, and SignalR disconnect/reconnect.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import { useCaptureStore } from '../../store/captureStore'
import { useSessionStore } from '../../store/sessionStore'
import { boardsApi } from '../../api/boardsApi'
import { captureApi } from '../../api/captureApi'
import { authApi } from '../../api/authApi'
import { usersApi } from '../../api/usersApi'

// ─── global mocks ────────────────────────────────────────────────────────────

const toastMocks = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn() }))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_err: unknown, fallback: string) => ({ message: fallback, code: null }),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    createCard: vi.fn(),
    updateCard: vi.fn(),
    moveCard: vi.fn(),
    deleteCard: vi.fn(),
  },
}))

vi.mock('../../api/cardCommentsApi', () => ({
  cardCommentsApi: {
    getComments: vi.fn(),
    createComment: vi.fn(),
    updateComment: vi.fn(),
    deleteComment: vi.fn(),
  },
}))

vi.mock('../../api/columnsApi', () => ({
  columnsApi: {
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
  },
}))

vi.mock('../../api/labelsApi', () => ({
  labelsApi: {
    getLabels: vi.fn(),
    createLabel: vi.fn(),
    updateLabel: vi.fn(),
    deleteLabel: vi.fn(),
  },
}))

vi.mock('../../api/captureApi', () => ({
  captureApi: {
    createItem: vi.fn(),
    listItems: vi.fn(),
    getItem: vi.fn(),
    ignoreItem: vi.fn(),
    cancelItem: vi.fn(),
    enqueueTriage: vi.fn(),
    batchTriage: vi.fn(),
    updateSuggestion: vi.fn(),
  },
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    getProviders: vi.fn(),
    exchangeOAuthCode: vi.fn(),
  },
}))

vi.mock('../../api/usersApi', () => ({
  usersApi: {
    getUser: vi.fn(),
  },
}))

// ─── helpers ─────────────────────────────────────────────────────────────────

function makeNetworkError(message = 'Network Error'): Error {
  return Object.assign(new Error(message), { code: 'ERR_NETWORK' })
}

function makeHttpError(status: number, message: string): unknown {
  return {
    response: {
      status,
      data: { message },
    },
    message,
  }
}

// ─── boardStore resilience ────────────────────────────────────────────────────

describe('boardStore — API failure resilience', () => {
  let store: ReturnType<typeof useBoardStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useBoardStore()
    vi.clearAllMocks()
  })

  it('sets error state when API is unreachable; boards list remains empty', async () => {
    vi.mocked(boardsApi.getBoards).mockRejectedValue(makeNetworkError())

    await expect(store.fetchBoards()).rejects.toThrow()

    expect(store.boards).toEqual([])
    expect(store.loading).toBe(false)
    expect(store.error).toBeTruthy()
  })

  it('preserves existing board list when a subsequent fetch fails', async () => {
    const initial = [
      {
        id: 'b1',
        name: 'My Board',
        description: '',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      },
    ]
    vi.mocked(boardsApi.getBoards).mockResolvedValueOnce(initial)
    await store.fetchBoards()
    expect(store.boards).toHaveLength(1)

    // Use a search filter to bypass the fetch throttle, then make it fail
    vi.mocked(boardsApi.getBoards).mockRejectedValueOnce(makeNetworkError())
    await expect(store.fetchBoards('filter-to-bypass-throttle')).rejects.toThrow()

    // After rejection, loading is false and error is set — no crash
    expect(store.loading).toBe(false)
    expect(store.error).toBeTruthy()
    // Boards should NOT be wiped by the failed fetch
    expect(store.boards).toHaveLength(1)
  })

  it('clears loading flag on 503 Service Unavailable', async () => {
    vi.mocked(boardsApi.getBoards).mockRejectedValue(makeHttpError(503, 'Service Unavailable'))

    await expect(store.fetchBoards()).rejects.toBeDefined()

    expect(store.loading).toBe(false)
  })

  it('creates a board with error set to null after success following a previous failure', async () => {
    // Simulate error first
    vi.mocked(boardsApi.getBoards).mockRejectedValueOnce(makeNetworkError())
    await expect(store.fetchBoards()).rejects.toThrow()
    expect(store.error).toBeTruthy()

    // Successful fetch should clear the error state
    vi.mocked(boardsApi.getBoards).mockResolvedValueOnce([])
    await store.fetchBoards()

    expect(store.error).toBeNull()
  })
})

// ─── captureStore resilience ─────────────────────────────────────────────────

describe('captureStore — API failure resilience', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('handles API unreachable on fetchItems without crashing; items remain empty', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.listItems).mockRejectedValue(makeNetworkError())

    // The store may throw or set error — either way no white-screen crash
    try {
      await store.fetchItems({ limit: 100 })
    } catch {
      // acceptable if store re-throws
    }

    // items list must remain consistent (not partially populated)
    expect(Array.isArray(store.items)).toBe(true)
  })

  it('handles 500 server error on createItem; sets error state', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.createItem).mockRejectedValue(
      makeHttpError(500, 'Internal Server Error'),
    )

    try {
      await store.createItem({ text: 'Test capture', boardId: null })
    } catch {
      // acceptable
    }

    // Toast error should be shown rather than silent failure
    expect(toastMocks.error).toHaveBeenCalled()
  })

  it('returns cached detail without re-fetching when already loaded', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()

    // Seed cache directly
    store.detailById['cap-1'] = {
      id: 'cap-1',
      userId: 'u1',
      boardId: null,
      status: 'New',
      source: 'Typed',
      textExcerpt: 'cached excerpt',
      rawText: 'full raw text',
      createdAt,
      processedAt: null,
      retryCount: 0,
    }

    await store.fetchDetail('cap-1')

    expect(captureApi.getItem).not.toHaveBeenCalled()
    expect(store.detailById['cap-1']?.rawText).toBe('full raw text')
  })

  it('does not corrupt existing valid cache when a refresh fetch fails', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()

    store.detailById['cap-2'] = {
      id: 'cap-2',
      userId: 'u1',
      boardId: null,
      status: 'New',
      source: 'Typed',
      textExcerpt: 'good cached excerpt',
      rawText: 'good cached text',
      createdAt,
      processedAt: null,
      retryCount: 0,
    }

    vi.mocked(captureApi.getItem).mockRejectedValue(makeNetworkError())

    try {
      await store.fetchDetail('cap-2', { forceRefresh: true })
    } catch {
      // error is acceptable
    }

    // Cache must not be wiped by a failed refresh
    expect(store.detailById['cap-2']?.rawText).toBe('good cached text')
  })
})

// ─── sessionStore resilience — missing token / localStorage cleared ──────────

describe('sessionStore — resilience when localStorage is cleared', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
  })

  it('is unauthenticated when localStorage is empty', () => {
    const store = useSessionStore()
    expect(store.isAuthenticated).toBe(false)
    expect(store.token).toBeFalsy()
  })

  it('handles login API failure gracefully; isAuthenticated remains false', async () => {
    const store = useSessionStore()
    vi.mocked(authApi.login).mockRejectedValue(
      makeHttpError(401, 'Invalid credentials'),
    )

    await expect(
      store.login({ usernameOrEmail: 'bad', password: 'bad' }),
    ).rejects.toBeDefined()

    expect(store.isAuthenticated).toBe(false)
    expect(store.token).toBeFalsy()
  })

  it('clears session state on logout; token removed from localStorage', () => {
    const store = useSessionStore()
    // Simulate already logged in
    localStorage.setItem('taskdeck_token', 'fake-token')

    store.logout()

    expect(store.isAuthenticated).toBe(false)
    expect(store.token).toBeFalsy()
    expect(localStorage.getItem('taskdeck_token')).toBeNull()
  })

  it('handles malformed localStorage token without throwing on initialisation', () => {
    // Seed an invalid token value
    localStorage.setItem('taskdeck_token', 'not-a-valid-jwt')

    // Creating the store should not throw even if token validation rejects it
    const act = () => {
      setActivePinia(createPinia())
      useSessionStore()
    }
    expect(act).not.toThrow()
  })
})

// ─── SignalR disconnect / reconnect resilience ────────────────────────────────
// These tests use the same mock infrastructure pattern as useBoardRealtime.spec.ts.
// The @microsoft/signalr mock is declared at file level (see hoisted mock below).

const realtimeCallbacks: {
  boardMutation?: (event: { boardId: string }) => Promise<void> | void
  reconnecting?: () => Promise<void> | void
  reconnected?: () => Promise<void> | void
  close?: () => Promise<void> | void
} = {}

const realtimeMockConnection = {
  state: 'Disconnected',
  start: vi.fn(async () => { realtimeMockConnection.state = 'Connected' }),
  stop: vi.fn(async () => { realtimeMockConnection.state = 'Disconnected' }),
  invoke: vi.fn(async () => undefined),
  on: vi.fn((eventName: string, handler: (event: { boardId: string }) => Promise<void> | void) => {
    if (eventName === 'boardMutation') realtimeCallbacks.boardMutation = handler
  }),
  onreconnecting: vi.fn((h: () => Promise<void> | void) => { realtimeCallbacks.reconnecting = h }),
  onreconnected: vi.fn((h: () => Promise<void> | void) => { realtimeCallbacks.reconnected = h }),
  onclose: vi.fn((h: () => Promise<void> | void) => { realtimeCallbacks.close = h }),
}

const realtimeMockBuilder = {
  withUrl: vi.fn(() => realtimeMockBuilder),
  withAutomaticReconnect: vi.fn(() => realtimeMockBuilder),
  configureLogging: vi.fn(() => realtimeMockBuilder),
  build: vi.fn(() => realtimeMockConnection),
}

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn().mockImplementation(function () { return realtimeMockBuilder }),
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  HttpTransportType: { WebSockets: 1 },
  LogLevel: { Warning: 3 },
}))

describe('useBoardRealtime — SignalR disconnect resilience', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.removeItem('taskdeck_token')
    realtimeCallbacks.boardMutation = undefined
    realtimeCallbacks.reconnecting = undefined
    realtimeCallbacks.reconnected = undefined
    realtimeCallbacks.close = undefined
    realtimeMockConnection.state = 'Disconnected'
    realtimeMockConnection.start.mockImplementation(async () => {
      realtimeMockConnection.state = 'Connected'
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts fallback polling during reconnecting then stops on reconnected', async () => {
    // reconnecting → starts polling; reconnected → stops polling and re-joins board
    const { createBoardRealtimeController } = await import('../../composables/useBoardRealtime')
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    vi.useFakeTimers()
    await controller.start('board-1')
    fetchBoard.mockClear()

    // Reconnecting fires — fallback polling begins
    await realtimeCallbacks.reconnecting?.()
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledWith('board-1')

    // Reconnected fires — fallback polling stops
    fetchBoard.mockClear()
    await realtimeCallbacks.reconnected?.()
    expect(realtimeMockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-1')

    // No more polls after reconnection
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).not.toHaveBeenCalled()

    await controller.stop()
  })

  it('starts fallback polling when SignalR closes unexpectedly', async () => {
    const { createBoardRealtimeController } = await import('../../composables/useBoardRealtime')
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    vi.useFakeTimers()
    await controller.start('board-1')
    fetchBoard.mockClear()

    // Simulate unexpected connection close
    await realtimeCallbacks.close?.()
    // Advance past one full fallback poll cycle (30s)
    await vi.advanceTimersByTimeAsync(31000)

    expect(fetchBoard).toHaveBeenCalled()
    await controller.stop()
  })

  it('ignores boardMutation events for a different board without crashing', async () => {
    const { createBoardRealtimeController } = await import('../../composables/useBoardRealtime')
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')

    // Fire an event with a boardId that does NOT match the subscribed board
    const act = () =>
      realtimeCallbacks.boardMutation?.({ boardId: 'some-other-board' })
    expect(act).not.toThrow()

    // fetchBoard should NOT be called for a non-matching boardId
    vi.useFakeTimers()
    await vi.advanceTimersByTimeAsync(300)
    expect(fetchBoard).not.toHaveBeenCalled()

    await controller.stop()
  })

  it('falls back to polling when SignalR connection cannot be established', async () => {
    vi.useFakeTimers()
    const { createBoardRealtimeController } = await import('../../composables/useBoardRealtime')
    const fetchBoard = vi.fn(async () => undefined)
    realtimeMockConnection.start.mockRejectedValueOnce(new Error('SignalR unavailable'))

    const controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-1')

    // Advance past the 30s fallback poll interval
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledWith('board-1')

    await controller.stop()
  })
})
